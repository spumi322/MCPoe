using System.Text.Json;
using MCPoe.Infrastructure.PoB;
using Microsoft.Extensions.Configuration;
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
    public async Task Pob_preview_tool_flow_returns_real_info_stats_and_xml()
    {
        Skip.IfNot(EnvAvailable, "LuaJIT or PoB fork not present on this machine");

        await using var engine = CreateEngine();
        var service = CreateService(engine);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        using var status = AssertToolOk(await service.GetStatusAsync(cts.Token));
        using var created = AssertToolOk(await service.NewBuildAsync(cts.Token));

        using var info = AssertToolOk(await service.GetBuildInfoAsync(cts.Token));
        Assert.Equal(JsonValueKind.Object, FirstResult(info).GetProperty("info").ValueKind);

        using var stats = AssertToolOk(await service.GetStatsAsync(["Life"], cts.Token));
        Assert.True(FirstResult(stats).GetProperty("stats").GetProperty("Life").GetDouble() > 0);

        using var exported = AssertToolOk(await service.ExportBuildXmlAsync(cts.Token));
        Assert.False(string.IsNullOrWhiteSpace(FirstResult(exported).GetProperty("xml").GetString()));
    }

    private static PoBEngineManager CreateEngine()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoB:LuaJitPath"] = LuaJitPath,
                ["PoB:SourcePath"] = PobSrcPath,
            })
            .Build();

        return new PoBEngineManager(NullLogger<PoBEngineManager>.Instance, configuration);
    }

    private static PoBService CreateService(PoBEngineManager engine) =>
        new(engine, NullLogger<PoBService>.Instance);

    private static JsonDocument AssertToolOk(string json)
    {
        var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.True(FirstResult(doc).GetProperty("ok").GetBoolean());
        return doc;
    }

    private static JsonElement FirstResult(JsonDocument doc) =>
        doc.RootElement.GetProperty("results")[0];
}
