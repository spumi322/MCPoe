using System.Reflection;
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
        var modsPath = ResolvePath(_configuration["Database:ModsPath"] ?? "data/mods.db");

        EnsureDatabase(vectorsPath);
        EnsureDatabase(modsPath);
        ApplyModsSchema(modsPath);
    }

    public static string ResolveModsPath(IConfiguration configuration) =>
        ResolvePath(configuration["Database:ModsPath"] ?? "data/mods.db");

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

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

    private void ApplyModsSchema(string modsPath)
    {
        var ddl = LoadEmbeddedSchema();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = modsPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
        _logger.LogInformation("Applied mods.db schema");
    }

    private static string LoadEmbeddedSchema()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("mods-schema.sql", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
