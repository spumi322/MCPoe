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
    [Description("Start or ping the process-scoped Path of Building engine and return MCPoe PoB state metadata.")]
    public Task<string> GetStatusAsync(CancellationToken ct) =>
        _pobService.GetStatusAsync(ct);

    [McpServerTool(Name = "pob_new_build")]
    [Description("Create or reset the current process-scoped Path of Building build.")]
    public Task<string> NewBuildAsync(CancellationToken ct) =>
        _pobService.NewBuildAsync(ct);

    [McpServerTool(Name = "pob_import_build")]
    [Description("Import a Path of Building build from a local .xml file path. This preview does not import URLs, PoB share codes, pasted raw XML, or text files.")]
    public Task<string> ImportBuildAsync(
        [Description("Local Path of Building .xml file path.")] string source,
        [Description("Optional display name for the loaded build.")] string? name = null,
        CancellationToken ct = default) =>
        _pobService.ImportBuildAsync(source, name, ct);

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

    [McpServerTool(Name = "pob_get_config")]
    [Description("Return current Path of Building config values such as bandit, pantheon, and enemy level.")]
    public Task<string> GetConfigAsync(CancellationToken ct) =>
        _pobService.GetConfigAsync(ct);

    [McpServerTool(Name = "pob_get_tree")]
    [Description("Return the current Path of Building passive tree allocation.")]
    public Task<string> GetTreeAsync(CancellationToken ct) =>
        _pobService.GetTreeAsync(ct);

    [McpServerTool(Name = "pob_search_nodes")]
    [Description("Search passive tree nodes in the currently loaded Path of Building build.")]
    public Task<string> SearchNodesAsync(
        [Description("Search text matched against passive node names or stat text.")] string keyword,
        [Description("Optional passive node type filter.")] string? nodeType = null,
        [Description("Optional maximum result count.")] int? maxResults = null,
        [Description("Whether allocated nodes should be included in results.")] bool? includeAllocated = null,
        CancellationToken ct = default) =>
        _pobService.SearchNodesAsync(keyword, nodeType, maxResults, includeAllocated, ct);

    [McpServerTool(Name = "pob_get_items")]
    [Description("Return equipped items, item slots, item text, and flask state for the currently loaded Path of Building build.")]
    public Task<string> GetItemsAsync(CancellationToken ct) =>
        _pobService.GetItemsAsync(ct);

    [McpServerTool(Name = "pob_get_skills")]
    [Description("Return skill socket groups and gems for the currently loaded Path of Building build.")]
    public Task<string> GetSkillsAsync(CancellationToken ct) =>
        _pobService.GetSkillsAsync(ct);

    [McpServerTool(Name = "pob_export_build_xml")]
    [Description("Export raw XML for the currently loaded Path of Building build.")]
    public Task<string> ExportBuildXmlAsync(CancellationToken ct) =>
        _pobService.ExportBuildXmlAsync(ct);
}
