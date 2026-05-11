namespace MCPoe.Application.Interfaces;

public interface IPoBService
{
    Task<string> CalculateNewBuildAsync(CancellationToken ct);
}
