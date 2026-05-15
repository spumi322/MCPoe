using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.Wiki;

public sealed class VoyageEmbeddingService : IEmbeddingService
{
    private const string Endpoint = "https://api.voyageai.com/v1/embeddings";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VoyageEmbeddingService> _logger;

    public VoyageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<VoyageEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<float[]> EmbedQueryAsync(string text, string model, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Voyage:ApiKey"]
            ?? _configuration["ApiKeys:VoyageApiKey"]
            ?? Environment.GetEnvironmentVariable("VOYAGE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Voyage API key is not configured. Set VOYAGE_API_KEY, Voyage:ApiKey, or ApiKeys:VoyageApiKey.");
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var request = new VoyageEmbeddingRequest([text], model, "query", true);
        using var response = await client.PostAsJsonAsync(Endpoint, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Voyage embeddings failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<VoyageEmbeddingResponse>(cancellationToken);
        if (parsed is null || parsed.Data.Count != 1)
        {
            throw new InvalidOperationException($"Voyage returned {parsed?.Data.Count ?? 0} embeddings for 1 query.");
        }

        var embedding = parsed.Data.OrderBy(d => d.Index).First().Embedding;
        _logger.LogInformation("Embedded wiki query with {Model}; dim={Dimensions}", model, embedding.Length);
        return embedding;
    }

    private sealed record VoyageEmbeddingRequest(
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("truncation")] bool Truncation);

    private sealed record VoyageEmbeddingResponse(
        [property: JsonPropertyName("data")] List<VoyageEmbeddingItem> Data);

    private sealed record VoyageEmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
