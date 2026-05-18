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
    public async Task Pob_preview_tool_flow_exports_and_reimports_xml()
    {
        Skip.IfNot(EnvAvailable, "LuaJIT or PoB fork not present on this machine");

        await using var engine = CreateEngine();
        var service = CreateService(engine);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        using var status = AssertToolOk(await service.GetStatusAsync(cts.Token));
        using var created = AssertToolOk(await service.NewBuildAsync(cts.Token));

        using var exported = AssertToolOk(await service.ExportBuildXmlAsync(cts.Token));
        var xml = FirstResult(exported).GetProperty("xml").GetString();
        Assert.False(string.IsNullOrWhiteSpace(xml));

        var xmlPath = Path.Combine(Path.GetTempPath(), "mcpoe-import-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(xmlPath, xml, cts.Token);

            using var imported = AssertToolOk(await service.ImportBuildAsync(xmlPath, "Imported Test", cts.Token));
            var importResult = FirstResult(imported);
            Assert.True(importResult.GetProperty("import").GetProperty("loaded").GetBoolean());
            Assert.Equal("local_xml_file", importResult.GetProperty("import").GetProperty("sourceType").GetString());
            Assert.True(importResult.GetProperty("loadResult").GetProperty("ok").GetBoolean());
            Assert.True(imported.RootElement.GetProperty("metadata").GetProperty("session").GetProperty("hasLoadedBuild").GetBoolean());
        }
        finally
        {
            if (File.Exists(xmlPath))
                File.Delete(xmlPath);
        }
    }

    [SkippableFact]
    public async Task Pob_exec_round_trips_through_bridge_and_reads_loaded_build()
    {
        Skip.IfNot(EnvAvailable, "LuaJIT or PoB fork not present on this machine");

        await using var engine = CreateEngine();
        var service = CreateService(engine);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        using var status = AssertToolOk(await service.GetStatusAsync(cts.Token));
        using var created = AssertToolOk(await service.NewBuildAsync(cts.Token));
        using var exported = AssertToolOk(await service.ExportBuildXmlAsync(cts.Token));
        var xml = FirstResult(exported).GetProperty("xml").GetString();
        Assert.False(string.IsNullOrWhiteSpace(xml));

        var xmlPath = Path.Combine(Path.GetTempPath(), "mcpoe-exec-validation-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            await File.WriteAllTextAsync(xmlPath, xml, cts.Token);
            using var imported = AssertToolOk(await service.ImportBuildAsync(xmlPath, "Phase 4 Imported", cts.Token));
            Assert.True(imported.RootElement.GetProperty("metadata").GetProperty("session").GetProperty("hasLoadedBuild").GetBoolean());

            using var scalar = await engine.ExecuteAsync("exec", new { code = "return { ok = true, lua = _VERSION }" }, cts.Token);
            Assert.True(scalar.Response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("Lua 5.1", scalar.Response.RootElement.GetProperty("value").GetProperty("lua").GetString());

            const string buildStateSnippet = """
                return {
                  name = build and build.buildName,
                  level = build and build.characterLevel,
                  hasItemsTab = build and build.itemsTab ~= nil,
                  hasSkillsTab = build and build.skillsTab ~= nil,
                  hasCalcsTab = build and build.calcsTab ~= nil
                }
                """;
            using var buildState = await engine.ExecuteAsync("exec", new { code = buildStateSnippet }, cts.Token);
            var buildValue = AssertExecOk(buildState);
            Assert.True(buildValue.GetProperty("level").GetDouble() > 0);
            Assert.True(buildValue.GetProperty("hasItemsTab").GetBoolean());
            Assert.True(buildValue.GetProperty("hasSkillsTab").GetBoolean());
            Assert.True(buildValue.GetProperty("hasCalcsTab").GetBoolean());

            const string readoutSnippet = """
                if not build or not build.calcsTab then
                  return { ready = false }
                end
                if build.calcsTab.BuildOutput then
                  build.calcsTab:BuildOutput()
                end
                local out = build.calcsTab.mainOutput or {}
                return {
                  ready = true,
                  Life = out.Life,
                  EnergyShield = out.EnergyShield,
                  Armour = out.Armour,
                  Evasion = out.Evasion
                }
                """;
            using var readout = await engine.ExecuteAsync("exec", new { code = readoutSnippet }, cts.Token);
            var readoutValue = AssertExecOk(readout);
            Assert.True(readoutValue.GetProperty("ready").GetBoolean());
            Assert.True(readoutValue.GetProperty("Life").GetDouble() > 0);
            Assert.True(readoutValue.GetProperty("EnergyShield").GetDouble() >= 0);
            Assert.True(readoutValue.GetProperty("Armour").GetDouble() >= 0);
            Assert.True(readoutValue.GetProperty("Evasion").GetDouble() >= 0);

            using var failure = await engine.ExecuteAsync("exec", new { code = "error('phase4 validation failure')" }, cts.Token);
            Assert.False(failure.Response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("phase4 validation failure", failure.Response.RootElement.GetProperty("error").GetString());
            Assert.Contains("stack traceback", failure.Response.RootElement.GetProperty("traceback").GetString());

            using var pingAfterFailure = AssertToolOk(await service.GetStatusAsync(cts.Token));
            Assert.True(FirstResult(pingAfterFailure).GetProperty("pong").GetBoolean());
        }
        finally
        {
            if (File.Exists(xmlPath))
                File.Delete(xmlPath);
        }
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
        new(engine, new BuildImportSourceClassifier(), NullLogger<PoBService>.Instance);

    private static JsonDocument AssertToolOk(string json)
    {
        var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.True(FirstResult(doc).GetProperty("ok").GetBoolean());
        return doc;
    }

    private static JsonElement FirstResult(JsonDocument doc) =>
        doc.RootElement.GetProperty("results")[0];

    private static JsonElement AssertExecOk(PoBEngineCallResult result)
    {
        Assert.True(result.Response.RootElement.GetProperty("ok").GetBoolean());
        return result.Response.RootElement.GetProperty("value");
    }
}
