namespace MCPoe.Application.Interfaces;

public interface IEmbeddingService
{
    Task<float[]> EmbedQueryAsync(string text, string model, CancellationToken cancellationToken = default);
}
