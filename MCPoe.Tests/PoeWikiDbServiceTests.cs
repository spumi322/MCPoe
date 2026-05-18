using System.Text.Json;
using MCPoe.Infrastructure.PoeWikiDb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPoe.Tests;

public sealed class PoeWikiDbServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _dbPath;
    private readonly IConfiguration _config;

    public PoeWikiDbServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "mcpoe-poewikidb-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _dbPath = Path.Combine(_tmpDir, "poe_wiki.db");
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:PoeWikiDbPath"] = _dbPath,
            })
            .Build();
    }

    [Fact]
    public async Task QueryPoeWikiDatabaseAsync_returns_rows_and_caps_results()
    {
        await CreateFixtureDbAsync(rowCount: 105);
        var service = CreateService();

        var result = await service.QueryPoeWikiDatabaseAsync("SELECT id, name FROM items ORDER BY id");

        using var json = JsonDocument.Parse(result);
        Assert.Equal("OK", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("query_poe_wiki_database", json.RootElement.GetProperty("tool").GetString());
        Assert.Equal("db", json.RootElement.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.Equal("read", json.RootElement.GetProperty("metadata").GetProperty("category").GetString());
        Assert.Equal(100, json.RootElement.GetProperty("results").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("metadata").GetProperty("truncated").GetBoolean());
        Assert.Equal(100, json.RootElement.GetProperty("metadata").GetProperty("rowLimit").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("metadata").GetProperty("returnedRows").GetInt32());
        Assert.Equal("Item 1", json.RootElement.GetProperty("results")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task QueryPoeWikiDatabaseAsync_allows_with_queries()
    {
        await CreateFixtureDbAsync(rowCount: 3);
        var service = CreateService();

        var result = await service.QueryPoeWikiDatabaseAsync("""
            WITH item_count AS (
                SELECT COUNT(*) AS total FROM items
            )
            SELECT total FROM item_count
            """);

        using var json = JsonDocument.Parse(result);
        Assert.Equal("OK", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("results")[0].GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("DELETE FROM items")]
    [InlineData("SELECT id FROM items; SELECT id FROM mods")]
    [InlineData("PRAGMA table_info(items)")]
    [InlineData("SELECT id FROM items -- hidden")]
    public async Task QueryPoeWikiDatabaseAsync_rejects_unsafe_sql(string sql)
    {
        await CreateFixtureDbAsync(rowCount: 1);
        var service = CreateService();

        var result = await service.QueryPoeWikiDatabaseAsync(sql);

        using var json = JsonDocument.Parse(result);
        Assert.Equal("REJECTED", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("query_poe_wiki_database", json.RootElement.GetProperty("tool").GetString());
        Assert.Equal("db", json.RootElement.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.True(json.RootElement.TryGetProperty("error", out var error));
        Assert.False(error.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task QueryPoeWikiDatabaseAsync_returns_sql_error_for_bad_select()
    {
        await CreateFixtureDbAsync(rowCount: 1);
        var service = CreateService();

        var result = await service.QueryPoeWikiDatabaseAsync("SELECT missing_column FROM items");

        using var json = JsonDocument.Parse(result);
        Assert.Equal("SQL_ERROR", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("db", json.RootElement.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.Contains("missing_column", json.RootElement.GetProperty("error").GetProperty("reason").GetString());
    }

    [SkippableFact]
    public async Task QueryPoeWikiDatabaseAsync_can_read_real_database_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var realDbPath = Path.Combine(repoRoot, "data", "poe_wiki.db");
        Skip.IfNot(File.Exists(realDbPath), $"Real PoE Wiki database not found at {realDbPath}");
        Skip.If(new FileInfo(realDbPath).Length == 0, $"Real PoE Wiki database is empty at {realDbPath}");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:PoeWikiDbPath"] = realDbPath,
            })
            .Build();

        var service = new PoeWikiDbService(config, NullLogger<PoeWikiDbService>.Instance);
        var result = await service.QueryPoeWikiDatabaseAsync("""
            SELECT name, class_id
            FROM items
            WHERE name IS NOT NULL
            ORDER BY name
            LIMIT 1
            """);

        using var json = JsonDocument.Parse(result);
        Assert.Equal("OK", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("db", json.RootElement.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("results").GetArrayLength());
    }

    private PoeWikiDbService CreateService() =>
        new(_config, NullLogger<PoeWikiDbService>.Instance);

    private async Task CreateFixtureDbAsync(int rowCount)
    {
        var connStr = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        using var schema = conn.CreateCommand();
        schema.CommandText = """
            CREATE TABLE items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                class_id TEXT,
                drop_level INTEGER
            );

            CREATE TABLE mods (
                id TEXT PRIMARY KEY,
                stat_text TEXT
            );
            """;
        await schema.ExecuteNonQueryAsync();

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        for (var i = 1; i <= rowCount; i++)
        {
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO items(id, name, class_id, drop_level)
                VALUES ($id, $name, 'Currency', $drop_level);
                """;
            insert.Parameters.AddWithValue("$id", i);
            insert.Parameters.AddWithValue("$name", $"Item {i}");
            insert.Parameters.AddWithValue("$drop_level", i);
            await insert.ExecuteNonQueryAsync();
        }

        using var insertMod = conn.CreateCommand();
        insertMod.Transaction = tx;
        insertMod.CommandText = "INSERT INTO mods(id, stat_text) VALUES ('TestMod', '+# to maximum Life');";
        await insertMod.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup for Windows file handles.
        }
    }
}
