using System.Text.Json;
using MCPoe.Infrastructure.PoB;
using MCPoe.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace MCPoe.Tests;

public sealed class PoBImportBuildTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "mcpoe-import-service-tests-" + Guid.NewGuid().ToString("N"));

    public PoBImportBuildTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ImportBuildAsync_rejects_url_without_starting_engine()
    {
        await using var engine = CreateEngine();
        var service = CreateService(engine);

        using var doc = JsonDocument.Parse(await service.ImportBuildAsync("https://pobb.in/abc123", null, CancellationToken.None));

        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("pob", doc.RootElement.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.Equal("session", doc.RootElement.GetProperty("metadata").GetProperty("category").GetString());
        Assert.Equal("unsupported_source", doc.RootElement.GetProperty("metadata").GetProperty("errorCode").GetString());
        Assert.Equal("url", doc.RootElement.GetProperty("metadata").GetProperty("import").GetProperty("sourceType").GetString());
    }

    [Fact]
    public async Task ImportBuildAsync_rejects_invalid_local_xml_before_engine_load()
    {
        var path = Path.Combine(_tempDir, "bad.xml");
        File.WriteAllText(path, "not xml");

        await using var engine = CreateEngine();
        var service = CreateService(engine);

        using var doc = JsonDocument.Parse(await service.ImportBuildAsync(path, null, CancellationToken.None));

        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("invalid_local_xml", doc.RootElement.GetProperty("metadata").GetProperty("errorCode").GetString());
        Assert.Equal("local_xml_file", doc.RootElement.GetProperty("metadata").GetProperty("import").GetProperty("sourceType").GetString());
    }

    [Fact]
    public void Raw_load_build_xml_is_not_exposed_as_mcp_tool()
    {
        var method = typeof(PoBTool).GetMethod(nameof(PoBTool.LoadBuildXmlAsync));

        Assert.NotNull(method);
        Assert.Empty(method!.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false));
    }

    private static PoBEngineManager CreateEngine()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PoB:LuaJitPath"] = @"Z:\not-used\luajit.exe",
                ["PoB:SourcePath"] = @"Z:\not-used\PathOfBuilding\src",
            })
            .Build();

        return new PoBEngineManager(NullLogger<PoBEngineManager>.Instance, configuration);
    }

    private static PoBService CreateService(PoBEngineManager engine) =>
        new(engine, new BuildImportSourceClassifier(), NullLogger<PoBService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
