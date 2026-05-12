using MCPoe.Infrastructure.Database;
using MCPoe.Infrastructure.ModsDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace MCPoe.Tests;

public class HeadhunterRenderSample
{
    private readonly ITestOutputHelper _output;
    public HeadhunterRenderSample(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Print_lookup_output()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mcpoe-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:VectorsPath"] = Path.Combine(tmp, "vectors.db"),
            ["Database:ModsPath"] = Path.Combine(tmp, "mods.db"),
        }).Build();

        new DatabaseInitializer(config, NullLogger<DatabaseInitializer>.Instance).Initialize();
        using var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new HeadhunterSeeder(config, NullLogger<HeadhunterSeeder>.Instance,
                services.GetRequiredService<IHttpClientFactory>()).SeedAsync(cts.Token);
        }
        catch (HttpRequestException ex) { Skip.If(true, ex.Message); }

        var svc = new ModsDbService(config, NullLogger<ModsDbService>.Instance);
        _output.WriteLine(await svc.LookupItemAsync("Headhunter"));
    }
}
