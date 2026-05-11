using System.Text.Json;
using MCPoe.Infrastructure.PoB;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPoe.Tests;

public class PoBBridgeIntegrationTests
{
    private const string LuaJitPath = @"C:\Users\spumi\scoop\shims\luajit.exe";
    private const string PobSrcPath = @"G:\Code\utils\PathOfBuilding\src";

    private static bool EnvAvailable =>
        File.Exists(LuaJitPath) &&
        File.Exists(Path.Combine(PobSrcPath, "HeadlessWrapper.lua"));

    [SkippableFact]
    public async Task CalculateNewBuild_returns_real_stats_from_engine()
    {
        Skip.IfNot(EnvAvailable, "LuaJIT or PoB fork not present on this machine");

        var service = new PoBService(NullLogger<PoBService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var json = await service.CalculateNewBuildAsync(cts.Token);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Object, root.GetProperty("build_info").ValueKind);
        var stats = root.GetProperty("stats");
        Assert.Equal(JsonValueKind.Object, stats.ValueKind);
        Assert.True(stats.GetProperty("Life").GetDouble() > 0, "Life should be > 0 for a fresh build");
    }
}
