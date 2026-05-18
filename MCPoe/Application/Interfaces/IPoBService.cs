namespace MCPoe.Application.Interfaces;

public interface IPoBService
{
    Task<string> GetStatusAsync(CancellationToken ct);
    Task<string> NewBuildAsync(CancellationToken ct);
    Task<string> ImportBuildAsync(string source, string? name, CancellationToken ct);
    Task<string> LoadBuildXmlAsync(string xml, string? name, CancellationToken ct);
    Task<string> GetBuildInfoAsync(CancellationToken ct);
    Task<string> GetStatsAsync(string[]? fields, CancellationToken ct);
    Task<string> GetConfigAsync(CancellationToken ct);
    Task<string> GetTreeAsync(CancellationToken ct);
    Task<string> SearchNodesAsync(string keyword, string? nodeType, int? maxResults, bool? includeAllocated, CancellationToken ct);
    Task<string> GetItemsAsync(CancellationToken ct);
    Task<string> GetSkillsAsync(CancellationToken ct);
    Task<string> ExportBuildXmlAsync(CancellationToken ct);
}
