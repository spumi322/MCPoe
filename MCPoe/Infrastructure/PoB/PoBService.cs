using MCPoe.Application.Interfaces;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBService : IPoBService
{
    public Task<string> CalculateBuildAsync(string buildCode) => Task.FromResult("Not implemented");
}
