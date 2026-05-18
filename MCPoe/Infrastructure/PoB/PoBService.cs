using System.Text.Json;
using System.Text.Json.Nodes;
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
        => await ExecuteToolAsync("pob_status", "ping", "session", "pob.status", null, ct).ConfigureAwait(false);

    public async Task<string> NewBuildAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_new_build", "new_build", "session", "pob.new_build", null, ct).ConfigureAwait(false);

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
            return SerializeToolError("pob_load_build_xml", "debug_internal", "XML input is required.");

        try
        {
            object parameters = string.IsNullOrWhiteSpace(name)
                ? new { xml }
                : new { xml, name };

            return await ExecuteToolAsync("pob_load_build_xml", "load_build_xml", "debug_internal", "pob.load_build_xml", parameters, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return SerializeToolError("pob_load_build_xml", "debug_internal", ex);
        }
    }

    public async Task<string> GetBuildInfoAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_build_info", "get_build_info", "read", "pob.build_info", null, ct).ConfigureAwait(false);

    public async Task<string> GetStatsAsync(string[]? fields, CancellationToken ct)
    {
        var parameters = fields is { Length: > 0 } ? new { fields } : null;
        return await ExecuteToolAsync("pob_get_stats", "get_stats", "read", "pob.stats", parameters, ct).ConfigureAwait(false);
    }

    public async Task<string> GetConfigAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_config", "get_config", "read", "pob.config", null, ct).ConfigureAwait(false);

    public async Task<string> GetTreeAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_tree", "get_tree", "read", "pob.tree", null, ct).ConfigureAwait(false);

    public async Task<string> SearchNodesAsync(
        string keyword,
        string? nodeType,
        int? maxResults,
        bool? includeAllocated,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return SerializeToolError("pob_search_nodes", "read", "keyword is required.");

        var parameters = new Dictionary<string, object?>
        {
            ["keyword"] = keyword
        };

        if (!string.IsNullOrWhiteSpace(nodeType))
            parameters["nodeType"] = nodeType;
        if (maxResults is not null)
            parameters["maxResults"] = maxResults.Value;
        if (includeAllocated is not null)
            parameters["includeAllocated"] = includeAllocated.Value;

        return await ExecuteToolAsync("pob_search_nodes", "search_nodes", "read", "pob.tree_node_search", parameters, ct).ConfigureAwait(false);
    }

    public async Task<string> GetItemsAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_items", "get_items", "read", "pob.items", null, ct).ConfigureAwait(false);

    public async Task<string> GetSkillsAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_get_skills", "get_skills", "read", "pob.skills", null, ct).ConfigureAwait(false);

    public async Task<string> ExportBuildXmlAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_export_build_xml", "export_build_xml", "session", "pob.export_build_xml", null, ct).ConfigureAwait(false);

    private async Task<string> ExecuteToolAsync(
        string tool,
        string action,
        string category,
        string resultKind,
        object? parameters,
        CancellationToken ct)
    {
        try
        {
            using var result = await _engine.ExecuteAsync(action, parameters, ct).ConfigureAwait(false);
            if (!ResponseOk(result.Response))
                return SerializeToolError(tool, category, ErrorFrom(result.Response), result.Session);

            return SerializeToolResponse("ok", tool, category, result.Session, new[] { ResultWithKind(resultKind, result.Response.RootElement) });
        }
        catch (Exception ex)
        {
            return SerializeToolError(tool, category, ex);
        }
    }

    private string SerializeToolError(string tool, string category, Exception ex)
    {
        _logger.LogError(ex, "{Tool} failed", tool);
        return SerializeToolError(tool, category, ex.Message);
    }

    private string SerializeToolError(string tool, string category, string reason)
        => SerializeToolError(tool, category, reason, _engine.Snapshot);

    private static string SerializeToolError(string tool, string category, string reason, PoBSessionSnapshot pobState)
    {
        return McpToolResponse.Serialize(
            status: "error",
            tool: tool,
            metadata: PoBMetadata(category, pobState),
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
            tool: "pob_import_build",
            metadata: PoBMetadata("session", session ?? _engine.Snapshot, importMetadata, errorCode),
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
            tool: "pob_import_build",
            metadata: PoBMetadata("session", session, importMetadata),
            results: new[]
            {
                new
                {
                    kind = "pob.import_build",
                    ok = true,
                    import = importMetadata,
                    loadResult
                }
            });
    }

    private static string SerializeToolResponse(
        string status,
        string tool,
        string category,
        PoBSessionSnapshot pobState,
        object results)
    {
        return McpToolResponse.Serialize(
            status: status,
            tool: tool,
            metadata: PoBMetadata(category, pobState),
            results: results);
    }

    private static object PoBMetadata(
        string category,
        PoBSessionSnapshot pobState,
        object? import = null,
        string? errorCode = null) =>
        new
        {
            domain = "pob",
            category,
            pobState,
            import,
            errorCode
        };

    private static JsonObject ResultWithKind(string kind, JsonElement element)
    {
        var result = JsonNode.Parse(element.GetRawText()) as JsonObject ?? new JsonObject
        {
            ["value"] = JsonNode.Parse(element.GetRawText())
        };

        result["kind"] = kind;
        return result;
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
