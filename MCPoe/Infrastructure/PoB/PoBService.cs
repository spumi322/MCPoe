using System.Text.Json;
using MCPoe.Application.Interfaces;
using MCPoe.Application.Models;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBService : IPoBService
{
    private readonly PoBEngineManager _engine;
    private readonly ILogger<PoBService> _logger;

    public PoBService(PoBEngineManager engine, ILogger<PoBService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task<string> GetStatusAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_status", "ping", null, ct).ConfigureAwait(false);

    public async Task<string> NewBuildAsync(CancellationToken ct)
        => await ExecuteToolAsync("pob_new_build", "new_build", null, ct).ConfigureAwait(false);

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

    private static bool ResponseOk(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static string ErrorFrom(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("error", out var error)
            ? error.ToString()
            : doc.RootElement.GetRawText();
}
