using System.Globalization;
using System.Text;
using MCPoe.Application.Interfaces;
using MCPoe.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.ModsDb;

public sealed class ModsDbService : IModsDbService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModsDbService> _logger;

    public ModsDbService(IConfiguration configuration, ILogger<ModsDbService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string> SearchModsAsync(string query, string? itemClass = null) =>
        Task.FromResult("Not implemented yet. This tool will be available in P1.");

    public async Task<string> LookupItemAsync(string itemName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return "Error: itemName is required.";
        }

        var path = DatabaseInitializer.ResolveModsPath(_configuration);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        var item = await LoadItemAsync(conn, itemName, cancellationToken);
        if (item is null)
        {
            return $"No item named \"{itemName}\" found in mods.db.";
        }

        var mods = await LoadItemModsAsync(conn, item.PageId, cancellationToken);
        var stats = await LoadItemStatsAsync(conn, item.PageId, cancellationToken);
        var sells = await LoadSellPricesAsync(conn, item.PageId, cancellationToken);

        return Format(item, mods, stats, sells);
    }

    private static async Task<ItemRow?> LoadItemAsync(SqliteConnection conn, string itemName, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT page_id, name, class_id, rarity_id, base_item, drop_level, drop_level_maximum,
                   tags, flavour_text, implicit_stat_text, explicit_stat_text, stat_text
            FROM items
            WHERE name = $name COLLATE NOCASE
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", itemName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ItemRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private static async Task<List<ModRow>> LoadItemModsAsync(SqliteConnection conn, long pageId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT im.mod_id, im.is_implicit, im.is_random,
                   COALESCE(m.stat_text, im.text) AS effective_text,
                   m.generation_type, m.domain, m.required_level
            FROM item_mods im
            LEFT JOIN mods m ON m.id = im.mod_id
            WHERE im.page_id = $pid
            ORDER BY im.is_implicit DESC, im.mod_id;
            """;
        cmd.Parameters.AddWithValue("$pid", pageId);
        var rows = new List<ModRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ModRow(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6)));
        }

        foreach (var mod in rows)
        {
            mod.Stats = await LoadModStatsAsync(conn, mod.ModId, ct);
        }
        return rows;
    }

    private static async Task<List<StatRange>> LoadModStatsAsync(SqliteConnection conn, string modId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT stat_id, min, max FROM mod_stats WHERE mod_id = $mid ORDER BY stat_id;";
        cmd.Parameters.AddWithValue("$mid", modId);
        var rows = new List<StatRange>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new StatRange(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }
        return rows;
    }

    private static async Task<List<StatRange>> LoadItemStatsAsync(SqliteConnection conn, long pageId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT stat_id, min, max FROM item_stats WHERE page_id = $pid ORDER BY stat_id;";
        cmd.Parameters.AddWithValue("$pid", pageId);
        var rows = new List<StatRange>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new StatRange(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }
        return rows;
    }

    private static async Task<List<SellPrice>> LoadSellPricesAsync(SqliteConnection conn, long pageId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, amount FROM item_sell_prices WHERE page_id = $pid ORDER BY name;";
        cmd.Parameters.AddWithValue("$pid", pageId);
        var rows = new List<SellPrice>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new SellPrice(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1)));
        }
        return rows;
    }

    private static string Format(ItemRow item, List<ModRow> mods, List<StatRange> stats, List<SellPrice> sells)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(item.Name);
        if (!string.IsNullOrEmpty(item.Rarity)) sb.Append(" (").Append(item.Rarity).Append(')');
        sb.AppendLine();
        if (!string.IsNullOrEmpty(item.BaseItem)) sb.Append("Base: ").AppendLine(item.BaseItem);
        if (!string.IsNullOrEmpty(item.ClassId)) sb.Append("Class: ").AppendLine(item.ClassId);
        if (item.DropLevel is { } dl) sb.Append("Drop level: ").Append(dl.ToString(CultureInfo.InvariantCulture)).AppendLine();
        if (!string.IsNullOrWhiteSpace(item.FlavourText))
        {
            sb.AppendLine().Append("Flavour: ").AppendLine(WikiMarkup.Clean(item.FlavourText));
        }

        var implicits = mods.Where(m => m.IsImplicit).ToList();
        var explicits = mods.Where(m => !m.IsImplicit).ToList();

        if (implicits.Count > 0)
        {
            sb.AppendLine().AppendLine("## Implicit");
            foreach (var m in implicits) AppendMod(sb, m);
        }

        if (explicits.Count > 0)
        {
            sb.AppendLine().AppendLine("## Explicit");
            foreach (var m in explicits) AppendMod(sb, m);
        }

        if (stats.Count > 0)
        {
            sb.AppendLine().AppendLine("## Aggregated stat ranges (item level)");
            foreach (var s in stats)
            {
                sb.Append("- ").Append(s.StatId).Append(": ").AppendLine(FormatRange(s.Min, s.Max));
            }
        }

        if (sells.Count > 0)
        {
            sb.AppendLine().AppendLine("## Vendor sell price");
            foreach (var p in sells)
            {
                sb.Append("- ").Append(p.Name).Append(": ").AppendLine((p.Amount?.ToString(CultureInfo.InvariantCulture)) ?? "?");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendMod(StringBuilder sb, ModRow m)
    {
        sb.Append("- ").AppendLine(WikiMarkup.Clean(m.EffectiveText) is { Length: > 0 } t ? t : m.ModId);
        sb.Append("    (id: ").Append(m.ModId);
        if (m.IsRandom) sb.Append(", random");
        if (m.RequiredLevel is { } lvl and > 0) sb.Append(", req lvl ").Append(lvl.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(")");
        foreach (var s in m.Stats)
        {
            sb.Append("    • ").Append(s.StatId).Append(": ").AppendLine(FormatRange(s.Min, s.Max));
        }
    }

    private static string FormatRange(double? min, double? max)
    {
        if (min is null && max is null) return "n/a";
        if (min is null) return $"≤ {max!.Value.ToString(CultureInfo.InvariantCulture)}";
        if (max is null) return $"≥ {min.Value.ToString(CultureInfo.InvariantCulture)}";
        return min.Value == max.Value
            ? min.Value.ToString(CultureInfo.InvariantCulture)
            : $"{min.Value.ToString(CultureInfo.InvariantCulture)} – {max.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private sealed record ItemRow(
        long PageId,
        string Name,
        string? ClassId,
        string? Rarity,
        string? BaseItem,
        long? DropLevel,
        long? DropLevelMax,
        string? Tags,
        string? FlavourText,
        string? ImplicitStatText,
        string? ExplicitStatText,
        string? StatText);

    private sealed record ModRow(
        string ModId,
        bool IsImplicit,
        bool IsRandom,
        string? EffectiveText,
        long? GenerationType,
        long? Domain,
        long? RequiredLevel)
    {
        public List<StatRange> Stats { get; set; } = new();
    }

    private sealed record StatRange(string StatId, double? Min, double? Max);

    private sealed record SellPrice(string Name, long? Amount);
}
