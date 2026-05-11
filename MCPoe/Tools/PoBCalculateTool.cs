using System.ComponentModel;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class PoBCalculateTool
{
    private readonly IPoBService _pobService;
    private readonly ILogger<PoBCalculateTool> _logger;

    public PoBCalculateTool(IPoBService pobService, ILogger<PoBCalculateTool> logger)
    {
        _pobService = pobService;
        _logger = logger;
    }

    [McpServerTool(Name = "calculate_build")]
    [Description("Calculate DPS and stats for a Path of Exile build using the Path of Building engine.")]
    public async Task<string> CalculateBuildAsync(
        [Description("The pastebin/PoB build code to evaluate.")] string build_code)
    {
        try
        {
            _ = await _pobService.CalculateBuildAsync(build_code);
            return "Not implemented yet. This tool will be available in P1.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "calculate_build failed");
            return $"Error: {ex.Message}";
        }
    }
}
