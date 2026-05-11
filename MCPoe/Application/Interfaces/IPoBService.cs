namespace MCPoe.Application.Interfaces;

public interface IPoBService
{
    Task<string> CalculateBuildAsync(string buildCode);
}
