using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MCPoe.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.ModsDb;

/// <summary>
/// P1b POC seeder. Pulls the Headhunter rows from the poewiki Cargo API
/// and inserts them into mods.db. Runs once at startup if items is empty.
/// </summary>
public sealed class HeadhunterSeeder
{
    private const string CargoBase = "https://www.poewiki.net/w/api.php?action=cargoquery&format=json";
    private const string Where = "items.name=\"Headhunter\" AND items.rarity_id=\"unique\"";

    private readonly IConfiguration _configuration;
    private readonly ILogger<HeadhunterSeeder> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public HeadhunterSeeder(
        IConfiguration configuration,
        ILogger<HeadhunterSeeder> logger,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var path = DatabaseInitializer.ResolveModsPath(_configuration);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync(ct);

        if (await CountAsync(conn, "items") > 0)
        {
            _logger.LogInformation("mods.db already seeded; skipping");
            return;
        }

        _logger.LogInformation("Seeding mods.db with Headhunter from Cargo API");

        var http = _httpClientFactory.CreateClient("poewiki");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MCPoe/0.1 (POC; +https://github.com/spumi322/MCPoe)");

        var itemsRows = await CargoAsync(http, "items",
            "items._pageName=pageName,items._pageID=pageID,name,class_id,rarity_id,base_item,base_item_page," +
            "metadata_id,frame_type,size_x,size_y,drop_enabled,drop_level,drop_level_maximum,tags,is_in_game," +
            "is_drop_restricted,release_version,removal_version,inventory_icon,html,flavour_text," +
            "implicit_stat_text,explicit_stat_text,stat_text,drop_areas,drop_monsters,drop_text,acquisition_tags",
            joinOn: null, ct);

        var itemModsRows = await CargoAsync(http, "items,item_mods",
            "items._pageID=pageID,item_mods.id=mod_id,item_mods.text=text,item_mods.is_implicit=is_implicit,item_mods.is_random=is_random",
            joinOn: "items._pageID=item_mods._pageID", ct);

        var itemStatsRows = await CargoAsync(http, "items,item_stats",
            "items._pageID=pageID,item_stats.id=stat_id,item_stats.min=min,item_stats.max=max",
            joinOn: "items._pageID=item_stats._pageID", ct);

        var modsRows = await CargoAsync(http, "items,item_mods,mods",
            "mods.id=id,mods.stat_text=stat_text,mods.generation_type=generation_type,mods.domain=domain,mods.required_level=required_level",
            joinOn: "items._pageID=item_mods._pageID,item_mods.id=mods.id", ct);

        var modStatsRows = await CargoAsync(http, "items,item_mods,mods,mod_stats",
            "mods.id=mod_id,mod_stats.id=stat_id,mod_stats.min=min,mod_stats.max=max",
            joinOn: "items._pageID=item_mods._pageID,item_mods.id=mods.id,mods._pageName=mod_stats._pageName", ct);

        var sellRows = await CargoAsync(http, "items,item_sell_prices",
            "items._pageID=pageID,item_sell_prices.name=name,item_sell_prices.amount=amount",
            joinOn: "items._pageID=item_sell_prices._pageID", ct);

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        InsertItems(conn, tx, itemsRows);
        InsertItemMods(conn, tx, itemModsRows);
        InsertItemStats(conn, tx, itemStatsRows);
        InsertMods(conn, tx, modsRows);
        InsertModStats(conn, tx, modStatsRows);
        InsertSellPrices(conn, tx, sellRows);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Seed complete: {Items} items, {ItemMods} item_mods, {ItemStats} item_stats, {Mods} mods, {ModStats} mod_stats, {Sell} sell_prices",
            itemsRows.Count, itemModsRows.Count, itemStatsRows.Count, modsRows.Count, modStatsRows.Count, sellRows.Count);
    }

    private static async Task<long> CountAsync(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L, CultureInfo.InvariantCulture);
    }

    private async Task<List<Dictionary<string, JsonElement>>> CargoAsync(
        HttpClient http, string tables, string fields, string? joinOn, CancellationToken ct)
    {
        var url = $"{CargoBase}&tables={Uri.EscapeDataString(tables)}&fields={Uri.EscapeDataString(fields)}&where={Uri.EscapeDataString(Where)}&limit=500";
        if (!string.IsNullOrEmpty(joinOn))
        {
            url += $"&join_on={Uri.EscapeDataString(joinOn)}";
        }

        _logger.LogDebug("Cargo GET {Url}", url);

        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var body = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);

        var rows = new List<Dictionary<string, JsonElement>>();
        if (!doc.RootElement.TryGetProperty("cargoquery", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var entry in arr.EnumerateArray())
        {
            if (!entry.TryGetProperty("title", out var title)) continue;
            var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in title.EnumerateObject())
            {
                // Response keys use spaces; normalise to underscores so we can look up by field name.
                var key = prop.Name.Replace(' ', '_');
                dict[key] = prop.Value.Clone();
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static string? Str(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static long? Long(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : (long?)v.GetDouble(),
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }

    private static double? Dbl(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) => n,
            _ => null,
        };
    }

    private static void InsertItems(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT OR REPLACE INTO items (
                page_id, page_name, name, class_id, rarity_id, base_item, base_item_page, metadata_id,
                frame_type, size_x, size_y, drop_enabled, drop_level, drop_level_maximum, tags,
                is_in_game, is_drop_restricted, release_version, removal_version, inventory_icon,
                html, flavour_text, implicit_stat_text, explicit_stat_text, stat_text,
                drop_areas, drop_monsters, drop_text, acquisition_tags
            ) VALUES (
                $page_id, $page_name, $name, $class_id, $rarity_id, $base_item, $base_item_page, $metadata_id,
                $frame_type, $size_x, $size_y, $drop_enabled, $drop_level, $drop_level_maximum, $tags,
                $is_in_game, $is_drop_restricted, $release_version, $removal_version, $inventory_icon,
                $html, $flavour_text, $implicit_stat_text, $explicit_stat_text, $stat_text,
                $drop_areas, $drop_monsters, $drop_text, $acquisition_tags
            );
            """;
        foreach (var r in rows)
        {
            var pid = Long(r, "pageID");
            if (pid is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$page_id", pid.Value);
            cmd.Parameters.AddWithValue("$page_name", (object?)Str(r, "pageName") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$name", (object?)Str(r, "name") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$class_id", (object?)Str(r, "class_id") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rarity_id", (object?)Str(r, "rarity_id") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$base_item", (object?)Str(r, "base_item") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$base_item_page", (object?)Str(r, "base_item_page") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$metadata_id", (object?)Str(r, "metadata_id") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$frame_type", (object?)Str(r, "frame_type") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$size_x", (object?)Long(r, "size_x") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$size_y", (object?)Long(r, "size_y") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_enabled", (object?)Long(r, "drop_enabled") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_level", (object?)Long(r, "drop_level") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_level_maximum", (object?)Long(r, "drop_level_maximum") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tags", (object?)Str(r, "tags") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$is_in_game", (object?)Long(r, "is_in_game") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$is_drop_restricted", (object?)Long(r, "is_drop_restricted") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$release_version", (object?)Str(r, "release_version") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$removal_version", (object?)Str(r, "removal_version") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$inventory_icon", (object?)Str(r, "inventory_icon") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$html", (object?)Str(r, "html") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$flavour_text", (object?)Str(r, "flavour_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$implicit_stat_text", (object?)Str(r, "implicit_stat_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$explicit_stat_text", (object?)Str(r, "explicit_stat_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$stat_text", (object?)Str(r, "stat_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_areas", (object?)Str(r, "drop_areas") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_monsters", (object?)Str(r, "drop_monsters") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drop_text", (object?)Str(r, "drop_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$acquisition_tags", (object?)Str(r, "acquisition_tags") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertItemMods(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT OR REPLACE INTO item_mods (page_id, mod_id, text, is_implicit, is_random)
            VALUES ($page_id, $mod_id, $text, $is_implicit, $is_random);
            """;
        foreach (var r in rows)
        {
            var pid = Long(r, "pageID");
            var mid = Str(r, "mod_id");
            if (pid is null || mid is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$page_id", pid.Value);
            cmd.Parameters.AddWithValue("$mod_id", mid);
            cmd.Parameters.AddWithValue("$text", (object?)Str(r, "text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$is_implicit", Long(r, "is_implicit") ?? 0L);
            cmd.Parameters.AddWithValue("$is_random", Long(r, "is_random") ?? 0L);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertItemStats(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT INTO item_stats (page_id, stat_id, min, max)
            VALUES ($page_id, $stat_id, $min, $max);
            """;
        foreach (var r in rows)
        {
            var pid = Long(r, "pageID");
            var sid = Str(r, "stat_id");
            if (pid is null || sid is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$page_id", pid.Value);
            cmd.Parameters.AddWithValue("$stat_id", sid);
            cmd.Parameters.AddWithValue("$min", (object?)Dbl(r, "min") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$max", (object?)Dbl(r, "max") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertMods(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT OR REPLACE INTO mods (id, stat_text, generation_type, domain, required_level)
            VALUES ($id, $stat_text, $generation_type, $domain, $required_level);
            """;
        foreach (var r in rows)
        {
            var id = Str(r, "id");
            if (id is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$stat_text", (object?)Str(r, "stat_text") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$generation_type", (object?)Long(r, "generation_type") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$domain", (object?)Long(r, "domain") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$required_level", (object?)Long(r, "required_level") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertModStats(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT OR REPLACE INTO mod_stats (mod_id, stat_id, min, max)
            VALUES ($mod_id, $stat_id, $min, $max);
            """;
        foreach (var r in rows)
        {
            var mid = Str(r, "mod_id");
            var sid = Str(r, "stat_id");
            if (mid is null || sid is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$mod_id", mid);
            cmd.Parameters.AddWithValue("$stat_id", sid);
            cmd.Parameters.AddWithValue("$min", (object?)Dbl(r, "min") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$max", (object?)Dbl(r, "max") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void InsertSellPrices(SqliteConnection conn, SqliteTransaction tx, List<Dictionary<string, JsonElement>> rows)
    {
        const string sql = """
            INSERT OR REPLACE INTO item_sell_prices (page_id, name, amount)
            VALUES ($page_id, $name, $amount);
            """;
        foreach (var r in rows)
        {
            var pid = Long(r, "pageID");
            var name = Str(r, "name");
            if (pid is null || name is null) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$page_id", pid.Value);
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$amount", (object?)Long(r, "amount") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }
}
