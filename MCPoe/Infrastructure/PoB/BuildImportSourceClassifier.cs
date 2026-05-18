namespace MCPoe.Infrastructure.PoB;

public enum BuildImportSourceType
{
    Empty,
    LocalXmlFile,
    UnsupportedFile,
    MissingPath,
    Url,
    RawXml,
    CompactText,
}

public sealed record BuildImportSourceClassification(
    BuildImportSourceType SourceType,
    string Source,
    string? ResolvedPath = null);

public sealed class BuildImportSourceClassifier
{
    public BuildImportSourceClassification Classify(string? source)
    {
        var normalized = Normalize(source);
        if (string.IsNullOrWhiteSpace(normalized))
            return new BuildImportSourceClassification(BuildImportSourceType.Empty, string.Empty);

        if (File.Exists(normalized))
        {
            var resolved = Path.GetFullPath(normalized);
            var sourceType = string.Equals(Path.GetExtension(resolved), ".xml", StringComparison.OrdinalIgnoreCase)
                ? BuildImportSourceType.LocalXmlFile
                : BuildImportSourceType.UnsupportedFile;

            return new BuildImportSourceClassification(sourceType, normalized, resolved);
        }

        if (IsHttpUrl(normalized))
            return new BuildImportSourceClassification(BuildImportSourceType.Url, normalized);

        if (LooksLikeRawXml(normalized))
            return new BuildImportSourceClassification(BuildImportSourceType.RawXml, normalized);

        if (LooksLikePath(normalized))
            return new BuildImportSourceClassification(BuildImportSourceType.MissingPath, normalized, TryGetFullPath(normalized));

        return new BuildImportSourceClassification(BuildImportSourceType.CompactText, normalized);
    }

    public static string ToJsonName(BuildImportSourceType sourceType) =>
        sourceType switch
        {
            BuildImportSourceType.Empty => "empty",
            BuildImportSourceType.LocalXmlFile => "local_xml_file",
            BuildImportSourceType.UnsupportedFile => "unsupported_file",
            BuildImportSourceType.MissingPath => "missing_path",
            BuildImportSourceType.Url => "url",
            BuildImportSourceType.RawXml => "raw_xml",
            BuildImportSourceType.CompactText => "compact_text",
            _ => sourceType.ToString(),
        };

    private static string Normalize(string? source)
    {
        var trimmed = source?.Trim() ?? string.Empty;
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static bool IsHttpUrl(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool LooksLikeRawXml(string source) =>
        source.StartsWith("<", StringComparison.Ordinal) ||
        source.Contains("<PathOfBuilding", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePath(string source)
    {
        if (source.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return true;

        if (source.Contains('\\') || source.Contains('/'))
            return true;

        return source.Length >= 2 && char.IsAsciiLetter(source[0]) && source[1] == ':';
    }

    private static string? TryGetFullPath(string source)
    {
        try
        {
            return Path.GetFullPath(source);
        }
        catch
        {
            return null;
        }
    }
}
