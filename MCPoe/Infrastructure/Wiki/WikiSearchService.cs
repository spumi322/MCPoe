using MCPoe.Application.Interfaces;

namespace MCPoe.Infrastructure.Wiki;

public sealed class WikiSearchService : IWikiSearchService
{
    public Task<string> SearchAsync(string query) => Task.FromResult("Not implemented");
}
