using System.ComponentModel;
using System.Text.Json;
using MCPoe.Application.Interfaces;
using MCPoe.Resources;
using MCPoe.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPoe.Tests;

public sealed class PoeWikiDbMapResourceTests
{
    [Fact]
    public void ReadMap_returns_raw_schema_map_json()
    {
        var json = PoeWikiDbMapResource.ReadMap();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(155, doc.RootElement.GetProperty("tableCount").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("tables", out var tables));
        Assert.Contains("\"items\"", json);
        Assert.Contains("\"mods\"", json);
    }

    [Fact]
    public void GetPoeWikiDatabaseMap_returns_raw_map_in_unified_tool_response()
    {
        var tool = new PoeWikiDatabaseTool(new UnusedPoeWikiDbService(), NullLogger<PoeWikiDatabaseTool>.Instance);

        var result = tool.GetPoeWikiDatabaseMap();

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("OK", root.GetProperty("status").GetString());
        Assert.Equal("get_poe_wiki_database_map", root.GetProperty("tool").GetString());
        Assert.Equal("db", root.GetProperty("metadata").GetProperty("domain").GetString());
        Assert.Equal("read", root.GetProperty("metadata").GetProperty("category").GetString());
        Assert.Equal("application/json", root.GetProperty("metadata").GetProperty("mimeType").GetString());
        Assert.DoesNotContain('/', root.GetProperty("metadata").GetProperty("source").GetString());

        var dbMap = root.GetProperty("results")[0].GetProperty("dbMap");
        Assert.Equal(155, dbMap.GetProperty("tableCount").GetInt32());
        Assert.True(dbMap.TryGetProperty("tables", out var tables));
        Assert.True(tables.GetArrayLength() > 0);
    }

    [Fact]
    public void QueryPoeWikiDatabase_description_includes_rough_table_index()
    {
        var method = typeof(PoeWikiDatabaseTool).GetMethod(nameof(PoeWikiDatabaseTool.QueryPoeWikiDatabaseAsync));
        var description = method!
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .Cast<DescriptionAttribute>()
            .Single()
            .Description;

        Assert.Contains("Rough DB index", description);
        Assert.Contains("items, weapons, armours", description);
        Assert.Contains("mods, mod_stats, generic_stats", description);
        Assert.Contains("skill_gems", description);
        Assert.Contains("allflame_embers", description);
        Assert.Contains("parent__field", description);
        Assert.Contains("get_poe_wiki_database_map", description);
    }

    private sealed class UnusedPoeWikiDbService : IPoeWikiDbService
    {
        public Task<string> QueryPoeWikiDatabaseAsync(string sql, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
