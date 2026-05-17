using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.Database;

public sealed class DatabaseInitializer
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IConfiguration configuration, ILogger<DatabaseInitializer> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void Initialize()
    {
        var vectorsPath = ResolvePath(_configuration["Database:VectorsPath"] ?? "data/vectors.db");

        EnsureDatabase(vectorsPath);
    }

    public static string ResolvePoeWikiDbPath(IConfiguration configuration) =>
        ResolvePath(configuration["Database:PoeWikiDbPath"] ?? "data/poe_wiki.db");

    public static string ResolveVectorsPath(IConfiguration configuration) =>
        ResolvePath(configuration["Database:VectorsPath"] ?? "data/vectors.db");

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), path);
        if (File.Exists(cwdPath))
        {
            return cwdPath;
        }

        return Path.Combine(AppContext.BaseDirectory, path);
    }

    private void EnsureDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        connection.Close();

        _logger.LogInformation("SQLite database ready at {Path}", Path.GetFullPath(path));
    }
}
