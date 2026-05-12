using System.ComponentModel;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class LookupItemTool
{
    private readonly IModsDbService _modsDbService;
    private readonly ILogger<LookupItemTool> _logger;

    public LookupItemTool(IModsDbService modsDbService, ILogger<LookupItemTool> logger)
    {
        _modsDbService = modsDbService;
        _logger = logger;
    }

    [McpServerTool(Name = "lookup_item")]
    [Description(
        "Look up everything known about a Path of Exile item by its exact name from the local mods database. " +
        "Returns the item's class, base item, implicit and explicit modifiers (with stat text and value ranges), " +
        "aggregated stat ranges, and vendor sell price. Use this to answer natural-language questions like " +
        "\"what mods does <item> have?\", \"what's the life roll on <item>?\", or \"what does <item> do?\". " +
        "Currently only Headhunter is seeded (P1b POC).")]
    public async Task<string> LookupItemAsync(
        [Description("Exact item name, e.g. \"Headhunter\". Case-insensitive.")] string item_name)
    {
        try
        {
            return await _modsDbService.LookupItemAsync(item_name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "lookup_item failed for {Item}", item_name);
            return $"Error: {ex.Message}";
        }
    }
}
