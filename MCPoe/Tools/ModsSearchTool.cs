using System.ComponentModel;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class ModsSearchTool
{
    private readonly IModsDbService _modsDbService;
    private readonly ILogger<ModsSearchTool> _logger;

    public ModsSearchTool(IModsDbService modsDbService, ILogger<ModsSearchTool> logger)
    {
        _modsDbService = modsDbService;
        _logger = logger;
    }

    [McpServerTool(Name = "search_mods")]
    [Description("Search the item mods database for mod stats and item properties.")]
    public async Task<string> SearchModsAsync(
        [Description("Free-text query against the mods database.")] string query,
        [Description("Optional item class filter (e.g. \"Body Armour\", \"Ring\").")] string? item_class = null)
    {
        try
        {
            _ = await _modsDbService.SearchModsAsync(query, item_class);
            return "Not implemented yet. This tool will be available in P1.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "search_mods failed");
            return $"Error: {ex.Message}";
        }
    }
}
