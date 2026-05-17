using MCPoe.Application.Interfaces;
using MCPoe.Application.Models;
using MCPoe.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MCPoe.Infrastructure.Wiki;

public sealed class WikiSearchService : IWikiSearchService
{
    private const int DefaultLimit = 6;
    private const int MaxContentChars = 900;
    private const float MinVectorScore = 0.2f;

    private readonly IConfiguration _configuration;
    private readonly ILogger<WikiSearchService> _logger;
    private readonly IEmbeddingService _embeddingService;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private WikiVectorIndex? _cachedIndex;

    public WikiSearchService(
        IConfiguration configuration,
        ILogger<WikiSearchService> logger,
        IEmbeddingService embeddingService)
    {
        _configuration = configuration;
        _logger = logger;
        _embeddingService = embeddingService;
    }

    public async Task<string> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return FormatFailure("ERROR", query, "query is required.");
        }

        var path = DatabaseInitializer.ResolveVectorsPath(_configuration);
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        if (!await TableExistsAsync(conn, "wiki_chunks"))
        {
            _logger.LogWarning("vectors.db at {Path} does not contain wiki_chunks", path);
            return FormatFailure("ERROR", query, $"vectors.db at {path} does not contain wiki_chunks.");
        }

        if (!await TableExistsAsync(conn, "wiki_embeddings"))
        {
            _logger.LogWarning("vectors.db at {Path} does not contain wiki_embeddings", path);
            return FormatFailure("ERROR", query, $"vectors.db at {path} does not contain wiki_embeddings.");
        }

        var model = await ResolveEmbeddingModelAsync(conn);
        if (string.IsNullOrWhiteSpace(model))
        {
            return FormatFailure("ERROR", query, "no embedding model metadata found in vectors.db.");
        }

        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingService.EmbedQueryAsync(query, model);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wiki query embedding failed");
            return FormatFailure("ERROR", query, ex.Message);
        }

        var index = await GetVectorIndexAsync(path, model);
        var rows = SearchVectors(queryEmbedding, index.Rows, DefaultLimit);
        if (rows.Count == 0)
        {
            return FormatFailure("NO_RESULTS", query, $"wiki vector search completed but found no chunks at or above score {MinVectorScore.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }

        return FormatResults(query, model, index.Rows.Count, rows);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE name = $name AND type IN ('table', 'virtual table')
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", tableName);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<string?> ResolveEmbeddingModelAsync(SqliteConnection conn)
    {
        if (await TableExistsAsync(conn, "index_metadata"))
        {
            using var metadataCmd = conn.CreateCommand();
            metadataCmd.CommandText = "SELECT value FROM index_metadata WHERE key = 'embedding_model' LIMIT 1;";
            var metadataModel = await metadataCmd.ExecuteScalarAsync() as string;
            if (!string.IsNullOrWhiteSpace(metadataModel))
            {
                return metadataModel;
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT model
            FROM wiki_embeddings
            GROUP BY model
            ORDER BY COUNT(*) DESC
            LIMIT 1;
            """;
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<WikiVectorIndex> GetVectorIndexAsync(string path, string model)
    {
        var fullPath = Path.GetFullPath(path);
        var cached = _cachedIndex;
        if (cached is not null
            && string.Equals(cached.Path, fullPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(cached.Model, model, StringComparison.Ordinal))
        {
            return cached;
        }

        await _indexLock.WaitAsync();
        try
        {
            cached = _cachedIndex;
            if (cached is not null
                && string.Equals(cached.Path, fullPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(cached.Model, model, StringComparison.Ordinal))
            {
                return cached;
            }

            var loaded = await LoadVectorIndexAsync(fullPath, model);
            _cachedIndex = loaded;
            _logger.LogInformation("Loaded wiki vector index from {Path}; model={Model}; chunks={Count}", fullPath, model, loaded.Rows.Count);
            return loaded;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private static async Task<WikiVectorIndex> LoadVectorIndexAsync(string path, string model)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.title, c.section, c.content, c.theme, c.page_type, c.class_id,
                   c.rag_tier, c.clean_path, e.embedding
            FROM wiki_embeddings e
            JOIN wiki_chunks c ON c.chunk_id = e.chunk_id
            WHERE e.model = $model
              AND e.embedding_text_hash = c.embedding_text_hash
            ORDER BY c.rowid;
            """;
        cmd.Parameters.AddWithValue("$model", model);

        var rows = new List<WikiVectorRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var bytes = (byte[])reader["embedding"];
            rows.Add(new WikiVectorRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                FromFloat32Blob(bytes)));
        }

        return new WikiVectorIndex(path, model, rows);
    }

    private static List<WikiChunkResult> SearchVectors(float[] queryEmbedding, IReadOnlyList<WikiVectorRow> rows, int limit) =>
        rows.Select(r => new WikiChunkResult(
                r.Title,
                r.Section,
                r.Content,
                r.Theme,
                r.PageType,
                r.ClassId,
                r.RagTier,
                r.CleanPath,
                Cosine(queryEmbedding, r.Embedding)))
            .Where(r => r.Score > 0)
            .Where(r => r.Score >= MinVectorScore)
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

    private static string FormatResults(
        string query,
        string model,
        int searchedChunks,
        IReadOnlyList<WikiChunkResult> rows)
    {
        var metadata = new RetrievalInfo(
                Mode: "vector",
                EmbeddingModel: model,
                SearchedChunks: searchedChunks,
                ReturnedChunks: rows.Count,
                MinScore: MinVectorScore);

        var results = rows.Select((row, index) => new WikiSearchResult(
                Rank: index + 1,
                Title: row.Title,
                Section: row.Section,
                Type: $"{row.Theme}/{row.PageType}",
                ClassId: row.ClassId,
                RagTier: row.RagTier,
                Score: Math.Round(row.Score, 4),
                Source: row.CleanPath,
                Excerpt: Truncate(row.Content, MaxContentChars))).ToList();

        return McpToolResponse.Serialize(
            status: "OK",
            grounded: true,
            mustAnswerFromResults: true,
            instruction: "Answer only from the returned chunks. If the chunks do not contain the needed fact, say MCPoe wiki search did not provide enough evidence.",
            tool: "search_wiki",
            query: query.Trim(),
            metadata: metadata,
            results: results);
    }

    private static string FormatFailure(string status, string query, string reason)
    {
        return McpToolResponse.Serialize(
            status: status,
            grounded: false,
            mustAnswerFromResults: false,
            instruction: status == "NO_RESULTS"
                ? "Do not infer unsupported mechanics from this tool call. Tell the user MCPoe wiki search found no grounded result."
                : "Do not answer using MCPoe wiki data from this tool call. Tell the user the wiki search tool failed and include the reason.",
            tool: "search_wiki",
            query: query.Trim(),
            metadata: new { },
            results: Array.Empty<object>(),
            error: new McpToolError(reason));
    }

    private static string Truncate(string value, int maxChars)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxChars)
        {
            return normalized;
        }

        return normalized[..maxChars].TrimEnd() + "...";
    }

    private static float[] FromFloat32Blob(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException($"Invalid float32 embedding blob length: {bytes.Length}");
        }

        return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    private static float Cosine(float[] a, float[] b)
    {
        double dot = 0;
        double magA = 0;
        double magB = 0;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0 ? 0f : (float)(dot / denom);
    }

    private sealed record WikiVectorIndex(
        string Path,
        string Model,
        IReadOnlyList<WikiVectorRow> Rows);

    private sealed record WikiVectorRow(
        string Title,
        string Section,
        string Content,
        string Theme,
        string PageType,
        string? ClassId,
        string RagTier,
        string CleanPath,
        float[] Embedding);

    private sealed record WikiChunkResult(
        string Title,
        string Section,
        string Content,
        string Theme,
        string PageType,
        string? ClassId,
        string RagTier,
        string CleanPath,
        double Score);

    private sealed record RetrievalInfo(
        string Mode,
        string EmbeddingModel,
        int SearchedChunks,
        int ReturnedChunks,
        float MinScore);

    private sealed record WikiSearchResult(
        int Rank,
        string Title,
        string Section,
        string Type,
        string? ClassId,
        string RagTier,
        double Score,
        string Source,
        string Excerpt);

}
