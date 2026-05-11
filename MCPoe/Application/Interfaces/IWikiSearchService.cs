namespace MCPoe.Application.Interfaces;

public interface IWikiSearchService
{
    Task<string> SearchAsync(string query);
}
