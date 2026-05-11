using MCPoe.Application.Interfaces;

namespace MCPoe.Infrastructure.ModsDb;

public sealed class ModsDbService : IModsDbService
{
    public Task<string> SearchModsAsync(string query, string? itemClass = null) => Task.FromResult("Not implemented");
}
