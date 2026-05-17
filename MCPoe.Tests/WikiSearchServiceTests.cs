using MCPoe.Application.Interfaces;
using MCPoe.Infrastructure.Wiki;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace MCPoe.Tests;

public sealed class WikiSearchServiceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _vectorsPath;

    public WikiSearchServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "mcpoe-wiki-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _vectorsPath = Path.Combine(_tmpDir, "vectors.db");
    }

    [Fact]
    public async Task SearchAsync_returns_error_when_embeddings_table_is_missing()
    {
        await CreateFixtureDbAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:VectorsPath"] = _vectorsPath,
            })
            .Build();

        var service = new WikiSearchService(config, NullLogger<WikiSearchService>.Instance, new ThrowingEmbeddingService());
        var result = await service.SearchAsync("righteous fire damage");

        using var json = JsonDocument.Parse(result);
        Assert.Equal("ERROR", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("grounded").GetBoolean());
        Assert.False(json.RootElement.GetProperty("mustAnswerFromResults").GetBoolean());
        Assert.Equal("search_wiki", json.RootElement.GetProperty("tool").GetString());
        Assert.Contains("wiki_embeddings", json.RootElement.GetProperty("error").GetProperty("reason").GetString());
        Assert.Contains("Do not answer", json.RootElement.GetProperty("instruction").GetString());
    }

    [Fact]
    public async Task SearchAsync_prefers_vector_results_when_embeddings_are_available()
    {
        await CreateVectorFixtureDbAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:VectorsPath"] = _vectorsPath,
            })
            .Build();

        var service = new WikiSearchService(config, NullLogger<WikiSearchService>.Instance, new FixedEmbeddingService([1f, 0f]));
        var result = await service.SearchAsync("burning aura");

        using var json = JsonDocument.Parse(result);
        Assert.Equal("OK", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("grounded").GetBoolean());
        Assert.True(json.RootElement.GetProperty("mustAnswerFromResults").GetBoolean());
        Assert.Equal("search_wiki", json.RootElement.GetProperty("tool").GetString());
        Assert.Equal("vector", json.RootElement.GetProperty("metadata").GetProperty("mode").GetString());
        Assert.Equal("Righteous Fire", json.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
        Assert.DoesNotContain("Cold Snap", result);
    }

    [SkippableFact]
    public async Task SearchAsync_can_read_real_vectors_db_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var realVectorsPath = Path.Combine(repoRoot, "data", "vectors.db");
        Skip.IfNot(File.Exists(realVectorsPath), $"Real vectors.db not found at {realVectorsPath}");
        Skip.If(new FileInfo(realVectorsPath).Length == 0, $"Real vectors.db is empty at {realVectorsPath}");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:VectorsPath"] = realVectorsPath,
            })
            .Build();

        var service = new WikiSearchService(config, NullLogger<WikiSearchService>.Instance, new FixedEmbeddingService([1f, 0f]));
        var result = await service.SearchAsync("Righteous Fire");

        using var json = JsonDocument.Parse(result);
        Assert.Equal("OK", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("grounded").GetBoolean());
        Assert.Contains("Righteous Fire", result);
        Assert.Contains("source", result);
    }

    private async Task CreateFixtureDbAsync()
    {
        var connStr = new SqliteConnectionStringBuilder { DataSource = _vectorsPath }.ToString();
        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        using var schema = conn.CreateCommand();
        schema.CommandText = """
            CREATE TABLE wiki_chunks (
                chunk_id TEXT PRIMARY KEY,
                page_id INTEGER NULL,
                title TEXT NOT NULL,
                theme TEXT NOT NULL,
                page_type TEXT NOT NULL,
                class_id TEXT NULL,
                rag_tier TEXT NOT NULL,
                clean_path TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                section TEXT NOT NULL,
                content TEXT NOT NULL,
                embedding_text TEXT NOT NULL,
                content_length INTEGER NOT NULL,
                embedding_text_length INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                embedding_text_hash TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE wiki_chunks_fts USING fts5(
                title,
                section,
                content,
                content='wiki_chunks',
                content_rowid='rowid'
            );

            CREATE TRIGGER wiki_chunks_ai AFTER INSERT ON wiki_chunks BEGIN
                INSERT INTO wiki_chunks_fts(rowid, title, section, content)
                VALUES (new.rowid, new.title, new.section, new.content);
            END;
            """;
        await schema.ExecuteNonQueryAsync();

        using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO wiki_chunks(
                chunk_id, page_id, title, theme, page_type, class_id, rag_tier,
                clean_path, chunk_index, section, content, embedding_text,
                content_length, embedding_text_length, content_hash, embedding_text_hash)
            VALUES (
                'skill-1', 1, 'Righteous Fire', 'skills', 'skill', 'Spell', 'core',
                'dataset/clean_md/skills/1_Righteous Fire.md', 0, 'Mechanics',
                'Righteous Fire deals burning damage around the character.',
                'Title: Righteous Fire\n\nRighteous Fire deals burning damage.',
                57, 61, 'content-hash', 'embedding-hash');
            """;
        await insert.ExecuteNonQueryAsync();
    }

    private async Task CreateVectorFixtureDbAsync()
    {
        await CreateFixtureDbAsync();

        var connStr = new SqliteConnectionStringBuilder { DataSource = _vectorsPath }.ToString();
        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        using var schema = conn.CreateCommand();
        schema.CommandText = """
            CREATE TABLE index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE wiki_embeddings (
                chunk_id TEXT NOT NULL,
                model TEXT NOT NULL,
                embedding_text_hash TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                embedding BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                PRIMARY KEY(chunk_id, model)
            );

            INSERT INTO index_metadata(key, value)
            VALUES ('embedding_model', 'test-model');

            INSERT INTO wiki_chunks(
                chunk_id, page_id, title, theme, page_type, class_id, rag_tier,
                clean_path, chunk_index, section, content, embedding_text,
                content_length, embedding_text_length, content_hash, embedding_text_hash)
            VALUES (
                'skill-2', 2, 'Cold Snap', 'skills', 'skill', 'Spell', 'core',
                'dataset/clean_md/skills/2_Cold Snap.md', 0, 'Mechanics',
                'Cold Snap deals cold damage in an area.',
                'Title: Cold Snap\n\nCold Snap deals cold damage.',
                40, 47, 'content-hash-2', 'embedding-hash-2');
            """;
        await schema.ExecuteNonQueryAsync();

        await InsertEmbeddingAsync(conn, "skill-1", "test-model", "embedding-hash", [1f, 0f]);
        await InsertEmbeddingAsync(conn, "skill-2", "test-model", "embedding-hash-2", [0f, 1f]);
    }

    private static async Task InsertEmbeddingAsync(
        SqliteConnection conn,
        string chunkId,
        string model,
        string hash,
        float[] embedding)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO wiki_embeddings(chunk_id, model, embedding_text_hash, dimensions, embedding, created_at_utc)
            VALUES ($chunk_id, $model, $hash, $dimensions, $embedding, $created_at_utc);
            """;
        cmd.Parameters.AddWithValue("$chunk_id", chunkId);
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$dimensions", embedding.Length);
        cmd.Parameters.Add("$embedding", SqliteType.Blob).Value = ToFloat32Blob(embedding);
        cmd.Parameters.AddWithValue("$created_at_utc", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static byte[] ToFloat32Blob(IReadOnlyList<float> values)
    {
        var bytes = new byte[values.Count * sizeof(float)];
        for (var i = 0; i < values.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float), sizeof(float)), values[i]);
        }

        return bytes;
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

    private sealed class FixedEmbeddingService : IEmbeddingService
    {
        private readonly float[] _embedding;

        public FixedEmbeddingService(float[] embedding) => _embedding = embedding;

        public Task<float[]> EmbedQueryAsync(string text, string model, CancellationToken cancellationToken = default) =>
            Task.FromResult(_embedding);
    }

    private sealed class ThrowingEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedQueryAsync(string text, string model, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Test embedder is unavailable.");
    }
}
