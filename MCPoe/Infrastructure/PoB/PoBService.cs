using System.Text.Json;
using MCPoe.Application.Interfaces;
using MCPoe.Application.Models;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBService : IPoBService
{
    private const long MaxImportXmlBytes = 25L * 1024L * 1024L;

    private readonly PoBEngineManager _engine;
    private readonly BuildImportSourceClassifier _importSourceClassifier;
    private readonly ILogger<PoBService> _logger;

    public PoBService(
        PoBEngineManager engine,
        BuildImportSourceClassifier importSourceClassifier,
        ILogger<PoBService> logger)
    {
        _engine = engine;
        _importSourceClassifier = importSourceClassifier;
        _logger = logger;
    }

    public async Task<string> GetStatusAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_status", "ping", null, ct).ConfigureAwait(false);

    public async Task<string> NewBuildAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_new_build", "new_build", null, ct).ConfigureAwait(false);

    public async Task<string> ImportBuildAsync(string source, string? name, CancellationToken ct)
    {
        var classification = _importSourceClassifier.Classify(source);
        var importMetadata = ImportMetadata(classification);

        if (classification.SourceType != BuildImportSourceType.LocalXmlFile)
        {
            return SerializeImportError(
                "unsupported_source",
                "This preview only supports local .xml files.",
                importMetadata);
        }

        var resolvedPath = classification.ResolvedPath!;
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.Length > MaxImportXmlBytes)
            {
                return SerializeImportError(
                    "invalid_local_xml",
                    $"XML file is too large for this preview ({fileInfo.Length} bytes, limit {MaxImportXmlBytes} bytes).",
                    ImportMetadata(classification, xmlBytes: fileInfo.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SerializeImportError("read_failed", $"Failed to inspect XML file: {ex.Message}", importMetadata);
        }

        string xml;
        try
        {
            xml = await File.ReadAllTextAsync(resolvedPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SerializeImportError("read_failed", $"Failed to read XML file: {ex.Message}", importMetadata);
        }

        importMetadata = ImportMetadata(classification, xmlBytes: fileInfo.Length);
        var validationError = ValidateImportXml(xml);
        if (validationError is not null)
            return SerializeImportError("invalid_local_xml", validationError, importMetadata);

        try
        {
            object parameters = string.IsNullOrWhiteSpace(name)
                ? new { xml }
                : new { xml, name };

            using var result = await _engine.ExecuteAsync("load_build_xml", parameters, ct).ConfigureAwait(false);
            if (!ResponseOk(result.Response))
            {
                return SerializeImportError(
                    "load_failed",
                    $"PoB failed to load XML: {ErrorFrom(result.Response)}",
                    importMetadata,
                    result.Session);
            }

            return SerializeImportSuccess(
                ImportMetadata(classification, xmlBytes: fileInfo.Length, loaded: true),
                result.Session,
                Clone(result.Response.RootElement));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "pob_import_build failed to load local XML file");
            return SerializeImportError("load_failed", $"PoB failed to load XML: {ex.Message}", importMetadata);
        }
    }

    public async Task<string> LoadBuildXmlAsync(string xml, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return SerializeToolError("pob_load_build_xml", "load_build_xml", "XML input is required.");

        try
        {
            object parameters = string.IsNullOrWhiteSpace(name)
                ? new { xml }
                : new { xml, name };

            return await ExecuteToolAsync("pob_load_build_xml", "load_build_xml", parameters, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return SerializeToolError("pob_load_build_xml", "load_build_xml", ex);
        }
    }

    public async Task<string> GetBuildInfoAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_build_info", "get_build_info", null, ct).ConfigureAwait(false);

    public async Task<string> GetStatsAsync(string[]? fields, CancellationToken ct)
    {
        var parameters = fields is { Length: > 0 } ? new { fields } : null;
        return await ExecuteToolAsync("pob_get_stats", "get_stats", parameters, ct).ConfigureAwait(false);
    }

    public async Task<string> ExportBuildXmlAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_export_build_xml", "export_build_xml", null, ct).ConfigureAwait(false);

    private async Task<string> ExecuteToolAsync(string tool, string action, object? parameters, CancellationToken ct)
    {
        try
        {
            using var result = await _engine.ExecuteAsync(action, parameters, ct).ConfigureAwait(false);
            if (!ResponseOk(result.Response))
                return SerializeToolError(tool, action, ErrorFrom(result.Response), result.Session);

            return SerializeToolResponse("ok", tool, action, result.Session, new[] { Clone(result.Response.RootElement) });
        }
        catch (Exception ex)
        {
            return SerializeToolError(tool, action, ex);
        }
    }

    private string SerializeToolError(string tool, string query, Exception ex)
    {
        _logger.LogError(ex, "{Tool} failed", tool);
        return SerializeToolError(tool, query, ex.Message);
    }

    private string SerializeToolError(string tool, string query, string reason)
        => SerializeToolError(tool, query, reason, _engine.Snapshot);

    private static string SerializeToolError(string tool, string query, string reason, PoBSessionSnapshot session)
    {
        return McpToolResponse.Serialize(
            status: "error",
            grounded: true,
            mustAnswerFromResults: true,
            instruction: "PoB action failed. Explain the error and ask for the next valid PoB action if needed.",
            tool: tool,
            query: query,
            metadata: new { session },
            results: Array.Empty<object>(),
            error: new McpToolError(reason));
    }

    private string SerializeImportError(
        string errorCode,
        string reason,
        object importMetadata,
        PoBSessionSnapshot? session = null)
    {
        return McpToolResponse.Serialize(
            status: "error",
            grounded: true,
            mustAnswerFromResults: true,
            instruction: "Build import failed. Explain the error and ask for a valid local .xml file path if needed.",
            tool: "pob_import_build",
            query: "import_build",
            metadata: new
            {
                session = session ?? _engine.Snapshot,
                import = importMetadata,
                errorCode
            },
            results: Array.Empty<object>(),
            error: new McpToolError(reason));
    }

    private static string SerializeImportSuccess(
        object importMetadata,
        PoBSessionSnapshot session,
        JsonElement loadResult)
    {
        return McpToolResponse.Serialize(
            status: "ok",
            grounded: true,
            mustAnswerFromResults: true,
            instruction: "Use only the returned PoB-backed import metadata and load result.",
            tool: "pob_import_build",
            query: "import_build",
            metadata: new { session },
            results: new[]
            {
                new
                {
                    ok = true,
                    import = importMetadata,
                    loadResult
                }
            });
    }

    private static string SerializeToolResponse(
        string status,
        string tool,
        string query,
        PoBSessionSnapshot session,
        object results)
    {
        return McpToolResponse.Serialize(
            status: status,
            grounded: true,
            mustAnswerFromResults: true,
            instruction: "Use only the returned PoB-backed data. Do not infer unsupported build facts.",
            tool: tool,
            query: query,
            metadata: new { session },
            results: results);
    }

    private static JsonElement Clone(JsonElement element) => element.Clone();

    private static object ImportMetadata(
        BuildImportSourceClassification classification,
        long? xmlBytes = null,
        bool? loaded = null) =>
        new
        {
            sourceType = BuildImportSourceClassifier.ToJsonName(classification.SourceType),
            resolvedFrom = classification.ResolvedPath,
            xmlBytes,
            loaded,
            supported = classification.SourceType == BuildImportSourceType.LocalXmlFile
        };

    private static string? ValidateImportXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return "XML file is empty.";

        var trimmed = xml.AsSpan().TrimStart();
        if (trimmed.Length < 32)
            return "XML file is too small to be a Path of Building build.";

        if (!trimmed.StartsWith("<", StringComparison.Ordinal))
            return "XML file does not start with XML markup.";

        if (!xml.Contains("<PathOfBuilding", StringComparison.OrdinalIgnoreCase))
            return "File does not look like a Path of Building XML file.";

        return null;
    }

    private static bool ResponseOk(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static string ErrorFrom(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("error", out var error)
            ? error.ToString()
            : doc.RootElement.GetRawText();
}
