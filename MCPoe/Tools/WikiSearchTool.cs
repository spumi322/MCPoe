using System.ComponentModel;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class WikiSearchTool
{
    private readonly IWikiSearchService _wikiSearchService;
    private readonly ILogger<WikiSearchTool> _logger;

    public WikiSearchTool(IWikiSearchService wikiSearchService, ILogger<WikiSearchTool> logger)
    {
        _wikiSearchService = wikiSearchService;
        _logger = logger;
    }

    [McpServerTool(Name = "search_wiki")]
    [Description("Search the Path of Exile wiki for game mechanic information.")]
    public async Task<string> SearchWikiAsync(
        [Description("Free-text query against the Path of Exile wiki.")] string query)
    {
        try
        {
            _ = await _wikiSearchService.SearchAsync(query);
            return "Not implemented yet. This tool will be available in P1.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "search_wiki failed");
            return $"Error: {ex.Message}";
        }
    }
}
