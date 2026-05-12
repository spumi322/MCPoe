using MCPoe.Infrastructure.Database;
using MCPoe.Infrastructure.ModsDb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPoe.Tests;

/// <summary>
/// End-to-end POC test: applies the schema, seeds Headhunter from the live Cargo API
/// (skipped if offline), then exercises ModsDbService.LookupItemAsync.
/// </summary>
public class HeadhunterLookupTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly IConfiguration _config;

    public HeadhunterLookupTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "mcpoe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:VectorsPath"] = Path.Combine(_tmpDir, "vectors.db"),
                ["Database:ModsPath"] = Path.Combine(_tmpDir, "mods.db"),
            })
            .Build();
    }

    [SkippableFact]
    public async Task LookupItem_Headhunter_returns_seeded_mods_from_cargo_api()
    {
        new DatabaseInitializer(_config, NullLogger<DatabaseInitializer>.Instance).Initialize();

        using var services = new ServiceCollection()
            .AddHttpClient()
            .BuildServiceProvider();
        var httpFactory = services.GetRequiredService<IHttpClientFactory>();

        var seeder = new HeadhunterSeeder(_config, NullLogger<HeadhunterSeeder>.Instance, httpFactory);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await seeder.SeedAsync(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Skip.If(true, $"Cargo API unreachable: {ex.Message}");
        }

        var modsPath = DatabaseInitializer.ResolveModsPath(_config);
        var connStr = new SqliteConnectionStringBuilder { DataSource = modsPath }.ToString();
        await using (var conn = new SqliteConnection(connStr))
        {
            await conn.OpenAsync();
            Assert.Equal(1L, await ScalarLongAsync(conn, "SELECT COUNT(*) FROM items"));
            Assert.Equal(6L, await ScalarLongAsync(conn, "SELECT COUNT(*) FROM item_mods"));
            Assert.Equal(6L, await ScalarLongAsync(conn, "SELECT COUNT(*) FROM mods"));
            Assert.Equal(1L, await ScalarLongAsync(conn, "SELECT COUNT(*) FROM item_mods WHERE is_implicit=1"));
            Assert.Equal(2L, await ScalarLongAsync(conn, "SELECT COUNT(*) FROM item_sell_prices"));
        }

        var svc = new ModsDbService(_config, NullLogger<ModsDbService>.Instance);
        var output = await svc.LookupItemAsync("Headhunter");

        Assert.Contains("Headhunter", output);
        Assert.Contains("Leather Belt", output);
        Assert.Contains("When you Kill a Rare monster", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IncreasedLifeImplicitBelt1", output);
        Assert.Contains("Alchemy Shard", output);
    }

    [Fact]
    public async Task LookupItem_unknown_returns_not_found()
    {
        new DatabaseInitializer(_config, NullLogger<DatabaseInitializer>.Instance).Initialize();
        var svc = new ModsDbService(_config, NullLogger<ModsDbService>.Instance);
        var output = await svc.LookupItemAsync("NotARealItem");
        Assert.Contains("No item", output, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(v);
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
