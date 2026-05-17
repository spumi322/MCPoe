namespace MCPoe.Application.Interfaces;

public interface IPoBService
{
    Task<string> GetStatusAsync(CancellationToken ct);
    Task<string> NewBuildAsync(CancellationToken ct);
    Task<string> LoadBuildXmlAsync(string xml, string? name, CancellationToken ct);
    Task<string> GetBuildInfoAsync(CancellationToken ct);
    Task<string> GetStatsAsync(string[]? fields, CancellationToken ct);
    Task<string> ExportBuildXmlAsync(CancellationToken ct);
}
