namespace MCPoe.Application.Interfaces;

public interface IPoeWikiDbService
{
    Task<string> QueryPoeWikiDatabaseAsync(string sql, CancellationToken cancellationToken = default);
}
