using MCPoe.Infrastructure.PoB;

namespace MCPoe.Tests;

public sealed class BuildImportSourceClassifierTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "mcpoe-import-tests-" + Guid.NewGuid().ToString("N"));
    private readonly BuildImportSourceClassifier _classifier = new();

    public BuildImportSourceClassifierTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData(null, BuildImportSourceType.Empty)]
    [InlineData("", BuildImportSourceType.Empty)]
    [InlineData("   ", BuildImportSourceType.Empty)]
    [InlineData("https://pobb.in/abc123", BuildImportSourceType.Url)]
    [InlineData("<PathOfBuilding></PathOfBuilding>", BuildImportSourceType.RawXml)]
    [InlineData("eNrtVeryLikelyImportCode", BuildImportSourceType.CompactText)]
    public void Classify_non_file_sources(string? source, BuildImportSourceType expected)
    {
        var result = _classifier.Classify(source);

        Assert.Equal(expected, result.SourceType);
    }

    [Fact]
    public void Classify_existing_xml_file()
    {
        var path = Path.Combine(_tempDir, "build.XML");
        File.WriteAllText(path, "<PathOfBuilding></PathOfBuilding>");

        var result = _classifier.Classify($"\"{path}\"");

        Assert.Equal(BuildImportSourceType.LocalXmlFile, result.SourceType);
        Assert.Equal(Path.GetFullPath(path), result.ResolvedPath);
    }

    [Fact]
    public void Classify_existing_non_xml_file()
    {
        var path = Path.Combine(_tempDir, "build.txt");
        File.WriteAllText(path, "eNrtVeryLikelyImportCode");

        var result = _classifier.Classify(path);

        Assert.Equal(BuildImportSourceType.UnsupportedFile, result.SourceType);
        Assert.Equal(Path.GetFullPath(path), result.ResolvedPath);
    }

    [Fact]
    public void Classify_missing_path_that_looks_like_xml_file()
    {
        var path = Path.Combine(_tempDir, "missing.xml");

        var result = _classifier.Classify(path);

        Assert.Equal(BuildImportSourceType.MissingPath, result.SourceType);
        Assert.Equal(Path.GetFullPath(path), result.ResolvedPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
