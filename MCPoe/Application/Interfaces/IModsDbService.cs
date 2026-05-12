namespace MCPoe.Application.Interfaces;

public interface IModsDbService
{
    Task<string> SearchModsAsync(string query, string? itemClass = null);

    Task<string> LookupItemAsync(string itemName, CancellationToken cancellationToken = default);
}
