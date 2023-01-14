using System.Net.Http.Json;

namespace logs_worker.HttpClients;

public class ElasticSearchClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ElasticSearchClient> _logger;

    public ElasticSearchClient(HttpClient httpClient, ILogger<ElasticSearchClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task CreateIndex(string indexName, CancellationToken ct)
    {
        _logger.LogInformation("Creating index {index}", indexName);
        await _httpClient.PutAsync($"/{indexName}", JsonContent.Create(new { }), ct);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}