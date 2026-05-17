using System.ComponentModel;
using System.Text.Json;
using MCPoe.Application.Interfaces;
using MCPoe.Application.Models;
using MCPoe.Resources;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class PoeWikiDatabaseTool
{
    private readonly IPoeWikiDbService _poeWikiDbService;
    private readonly ILogger<PoeWikiDatabaseTool> _logger;

    public PoeWikiDatabaseTool(IPoeWikiDbService poeWikiDbService, ILogger<PoeWikiDatabaseTool> logger)
    {
        _poeWikiDbService = poeWikiDbService;
        _logger = logger;
    }

    private const string QueryToolDatabaseIndex =
        "Rough DB index for table routing: " +
        "Items/equipment: items, weapons, armours, shields, amulets, jewels, flasks, stackables, map_fragments, blight_items, corpse_items, idols, sentinels, tattoos, tinctures, item_buffs, legacy_variants. " +
        "Item acquisition/economy: acquisition_recipes, acquisition_recipe_parts, item_purchase_costs, item_sell_prices, mod_sell_prices, vendor_rewards, quest_rewards, divination_cards. " +
        "Mods/stats/weights: mods, mod_stats, generic_stats, item_mods, item_stats, mod_spawn_weights, mod_generation_weights, spawn_weights, fossil_weights. " +
        "Skills/gems: skill, skill_gems, skill_levels, gem_levels, skill_quality, skill_quality_stats, skill_stats_per_level. " +
        "Passive tree/masteries/classes: passive_skills, passive_skill_stats, passive_skill_connections, mastery_groups, mastery_effects, ascendancy_classes, character_classes. " +
        "Areas/maps/atlas: areas, maps, map_series, atlas_nodes, atlas_regions, atlas_base_item_types, synthesis_areas. " +
        "Monsters: monsters, monster_types, monster_base_stats, monster_life_scaling, monster_map_multipliers, monster_resistances. " +
        "Crafting: crafting_bench_options, crafting_bench_options_costs, essences, fossils, harvest_crafting_options, blight_crafting_recipes. " +
        "League systems: allflame_embers, delve_*, heist_*, blight_*, bestiary_*, incursion_rooms, synthesis_*, pantheon, pantheon_*. " +
        "Wiki/meta/reference: versions, guides, main_pages, _dbscraper_metadata. " +
        "Child/repeated-field tables: parent__field tables such as items__tags, mods__tags, monsters__skill_ids usually contain _rowID plus _value. ";

    [McpServerTool(Name = "query_poe_wiki_database")]
    [Description(
        "Run a read-only SQL SELECT query against the local structured Path of Exile Wiki database. " +
        QueryToolDatabaseIndex +
        "Use this rough index to choose likely tables before writing SQL. " +
        "Call get_poe_wiki_database_map when exact column names are uncertain, and before writing joins or multi-joins. " +
        "Use the full map's exact table names, columns, indexes, foreign keys, row counts, and createSql when precision is needed. " +
        "Use this for exact values, counts, filters, joins, lists, and relationships involving items, mods, stats, " +
        "skills, passives, monsters, maps, areas, crafting, rewards, spawn weights, and generation weights. " +
        "Use this instead of search_wiki when the answer needs table-backed facts. " +
        "Only SELECT/WITH queries are allowed and returned rows are capped.")]
    public async Task<string> QueryPoeWikiDatabaseAsync(
        [Description("Read-only SQLite query. Must start with SELECT or WITH. Use exact identifiers from get_poe_wiki_database_map.")] string sql)
    {
        try
        {
            return await _poeWikiDbService.QueryPoeWikiDatabaseAsync(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "query_poe_wiki_database failed");
            return McpToolResponse.Serialize(
                status: "ERROR",
                grounded: false,
                mustAnswerFromResults: false,
                instruction: "Do not answer from this tool call. The query_poe_wiki_database tool failed.",
                tool: "query_poe_wiki_database",
                query: sql,
                metadata: new { },
                results: Array.Empty<object>(),
                error: new McpToolError(ex.Message));
        }
    }

    [McpServerTool(Name = "get_poe_wiki_database_map")]
    [Description(
        "Return the full raw schema map for the local structured Path of Exile Wiki SQLite database. " +
        "Call this before query_poe_wiki_database when building SQL, especially joins or multi-joins. " +
        "Use the returned table names, columns, indexes, foreign keys, row counts, and createSql exactly. " +
        "This tool does not query game data; it only returns the database map used to write valid SQL.")]
    public string GetPoeWikiDatabaseMap()
    {
        try
        {
            var path = PoeWikiDbMapResource.ResolveMapPath();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var dbMap = document.RootElement.Clone();

            return McpToolResponse.Serialize(
                status: "OK",
                grounded: true,
                mustAnswerFromResults: true,
                instruction: "Use this DB map to write valid SELECT/WITH SQL for query_poe_wiki_database. Do not answer game-data questions from this map alone.",
                tool: "get_poe_wiki_database_map",
                query: string.Empty,
                metadata: new PoeWikiDbMapMetadata(path, "application/json"),
                results: new[] { new PoeWikiDbMapResult(dbMap) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "get_poe_wiki_database_map failed");
            return McpToolResponse.Serialize(
                status: "ERROR",
                grounded: false,
                mustAnswerFromResults: false,
                instruction: "Do not answer from this tool call. The PoE Wiki database map could not be read.",
                tool: "get_poe_wiki_database_map",
                query: string.Empty,
                metadata: new { },
                results: Array.Empty<object>(),
                error: new McpToolError(ex.Message));
        }
    }

    private sealed record PoeWikiDbMapMetadata(string Source, string MimeType);

    private sealed record PoeWikiDbMapResult(JsonElement DbMap);
}
