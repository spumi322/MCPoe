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
    [Description("Spin up a fresh Path of Building build via the headless engine and return its base stats. POC stage: build code import not yet supported.")]
    public async Task<string> CalculateBuildAsync(CancellationToken ct)
    {
        try
        {
            return await _pobService.CalculateNewBuildAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "calculate_build failed");
            return $"Error: {ex.Message}";
        }
    }
}
