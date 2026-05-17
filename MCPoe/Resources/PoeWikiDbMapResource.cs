using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace MCPoe.Resources;

[McpServerResourceType]
public sealed class PoeWikiDbMapResource
{
    public const string Uri = "poe-wiki-db-map://schema";

    [McpServerResource(
        UriTemplate = Uri,
        Name = "PoE Wiki DB Map",
        Title = "PoE Wiki DB Map",
        MimeType = "application/json")]
    public static string ReadMap()
    {
        var path = ResolveMapPath();
        return File.ReadAllText(path);
    }

    internal static string ResolveMapPath()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var configured = configuration["Database:PoeWikiDbMapPath"] ?? "data/poe_wiki_db_map.json";
        return ResolvePath(configured);
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return NormalizePath(path);
        }

        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), path);
        if (File.Exists(cwdPath))
        {
            return NormalizePath(cwdPath);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, path);
            if (File.Exists(candidate))
            {
                return NormalizePath(candidate);
            }

            current = current.Parent;
        }

        return NormalizePath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? fullPath
            : fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
