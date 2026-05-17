using System.ComponentModel;
using MCPoe.Application.Interfaces;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class PoBTool
{
    private readonly IPoBService _pobService;

    public PoBTool(IPoBService pobService)
    {
        _pobService = pobService;
    }

    [McpServerTool(Name = "pob_status")]
    [Description("Start or ping the process-scoped Path of Building engine and return MCPoe session metadata.")]
    public Task<string> GetStatusAsync(CancellationToken ct) =>
        _pobService.GetStatusAsync(ct);

    [McpServerTool(Name = "pob_new_build")]
    [Description("Create or reset the current process-scoped Path of Building build.")]
    public Task<string> NewBuildAsync(CancellationToken ct) =>
        _pobService.NewBuildAsync(ct);

    [McpServerTool(Name = "pob_load_build_xml")]
    [Description("Load raw Path of Building XML into the current process-scoped engine session. This is not share-code or pobb.in import.")]
    public Task<string> LoadBuildXmlAsync(
        [Description("Raw Path of Building XML content.")] string xml,
        [Description("Optional display name for the loaded build.")] string? name = null,
        CancellationToken ct = default) =>
        _pobService.LoadBuildXmlAsync(xml, name, ct);

    [McpServerTool(Name = "pob_get_build_info")]
    [Description("Return basic metadata for the currently loaded Path of Building build.")]
    public Task<string> GetBuildInfoAsync(CancellationToken ct) =>
        _pobService.GetBuildInfoAsync(ct);

    [McpServerTool(Name = "pob_get_stats")]
    [Description("Return calculated stats for the currently loaded Path of Building build. Optional fields limit the returned stats.")]
    public Task<string> GetStatsAsync(
        [Description("Optional PoB stat field names, for example Life or EnergyShield.")] string[]? fields = null,
        CancellationToken ct = default) =>
        _pobService.GetStatsAsync(fields, ct);

    [McpServerTool(Name = "pob_export_build_xml")]
    [Description("Export raw XML for the currently loaded Path of Building build.")]
    public Task<string> ExportBuildXmlAsync(CancellationToken ct) =>
        _pobService.ExportBuildXmlAsync(ct);
}

