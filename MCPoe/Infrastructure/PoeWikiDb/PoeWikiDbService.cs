using System.Globalization;
using System.Text.RegularExpressions;
using MCPoe.Application.Interfaces;
using MCPoe.Application.Models;
using MCPoe.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoeWikiDb;

public sealed class PoeWikiDbService : IPoeWikiDbService
{
    private const int DefaultRowLimit = 100;

    private static readonly string[] ForbiddenSqlTokens =
    [
        "INSERT",
        "UPDATE",
        "DELETE",
        "DROP",
        "ALTER",
        "CREATE",
        "REPLACE",
        "VACUUM",
        "ATTACH",
        "DETACH",
        "PRAGMA",
        "TRUNCATE",
    ];

    private readonly IConfiguration _configuration;
    private readonly ILogger<PoeWikiDbService> _logger;

    public PoeWikiDbService(IConfiguration configuration, ILogger<PoeWikiDbService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> QueryPoeWikiDatabaseAsync(string sql, CancellationToken cancellationToken = default)
    {
        var validation = ValidateReadOnlySql(sql);
        if (validation.Error is not null)
        {
            return McpToolResponse.Serialize(
                status: "REJECTED",
                grounded: false,
                mustAnswerFromResults: false,
                instruction: "Do not answer from this tool call. The SQL was rejected before execution.",
                tool: "query_poe_wiki_database",
                query: sql,
                metadata: new PoeWikiQueryMetadata("sql", DefaultRowLimit, 0, false),
                results: Array.Empty<object>(),
                error: new McpToolError(validation.Error));
        }

        var path = DatabaseInitializer.ResolvePoeWikiDbPath(_configuration);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        try
        {
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = validation.Sql!;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var columnNames = BuildUniqueColumnNames(reader);
            var rows = new List<Dictionary<string, object?>>();

            while (rows.Count < DefaultRowLimit && await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < columnNames.Count; i++)
                {
                    row[columnNames[i]] = ReadJsonSafeValue(reader, i);
                }

                rows.Add(row);
            }

            var truncated = await reader.ReadAsync(cancellationToken);
            return McpToolResponse.Serialize(
                status: "OK",
                grounded: true,
                mustAnswerFromResults: true,
                instruction: "Answer only from these SQL rows. If insufficient, call get_poe_wiki_database_map and run another SELECT.",
                tool: "query_poe_wiki_database",
                query: validation.Sql!,
                metadata: new PoeWikiQueryMetadata("sql", DefaultRowLimit, rows.Count, truncated),
                results: rows);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "PoE Wiki database SQL query failed");
            return McpToolResponse.Serialize(
                status: "SQL_ERROR",
                grounded: false,
                mustAnswerFromResults: false,
                instruction: "Do not answer from this tool call. Fix the SQL using get_poe_wiki_database_map and run another SELECT.",
                tool: "query_poe_wiki_database",
                query: validation.Sql!,
                metadata: new PoeWikiQueryMetadata("sql", DefaultRowLimit, 0, false),
                results: Array.Empty<object>(),
                error: new McpToolError(ex.Message));
        }
    }

    private static SqlValidationResult ValidateReadOnlySql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new(null, "SQL is required.");
        }

        var normalized = sql.Trim();
        if (normalized.Contains("--", StringComparison.Ordinal) || normalized.Contains("/*", StringComparison.Ordinal))
        {
            return new(null, "SQL comments are not allowed.");
        }

        if (normalized.EndsWith(';'))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        if (ContainsSemicolonOutsideQuotes(normalized))
        {
            return new(null, "Multiple SQL statements are not allowed.");
        }

        if (!Regex.IsMatch(normalized, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return new(null, "Only read-only SELECT or WITH queries are allowed.");
        }

        var keywordScan = StripQuotedLiterals(normalized);
        var tokens = Regex.Matches(keywordScan, @"[A-Za-z_]+")
            .Select(m => m.Value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var forbidden = ForbiddenSqlTokens.FirstOrDefault(tokens.Contains);
        return forbidden is null
            ? new(normalized, null)
            : new(null, $"Forbidden SQL keyword: {forbidden}.");
    }

    private static bool ContainsSemicolonOutsideQuotes(string sql)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch == '\'' && !inDouble)
            {
                if (inSingle && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (ch == ';' && !inSingle && !inDouble)
            {
                return true;
            }
        }

        return false;
    }

    private static string StripQuotedLiterals(string sql)
    {
        var chars = sql.ToCharArray();
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (ch == '\'' && !inDouble)
            {
                if (inSingle && i + 1 < chars.Length && chars[i + 1] == '\'')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++;
                    continue;
                }

                inSingle = !inSingle;
                chars[i] = ' ';
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
                chars[i] = ' ';
                continue;
            }

            if (inSingle || inDouble)
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static IReadOnlyList<string> BuildUniqueColumnNames(SqliteDataReader reader)
    {
        var names = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var baseName = string.IsNullOrWhiteSpace(reader.GetName(i)) ? $"column_{i + 1}" : reader.GetName(i);
            if (!seen.TryGetValue(baseName, out var count))
            {
                seen[baseName] = 1;
                names.Add(baseName);
                continue;
            }

            count++;
            seen[baseName] = count;
            names.Add($"{baseName}_{count}");
        }

        return names;
    }

    private static object? ReadJsonSafeValue(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private sealed record SqlValidationResult(string? Sql, string? Error);

    private sealed record PoeWikiQueryMetadata(
        string Mode,
        int RowLimit,
        int ReturnedRows,
        bool Truncated);
}
