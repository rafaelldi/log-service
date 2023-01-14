using System.Net;
using System.Net.Http.Json;

namespace logs_worker.HttpClients;

public class KibanaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KibanaClient> _logger;

    public KibanaClient(HttpClient httpClient, ILogger<KibanaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsDataViewExists(string dataViewId, CancellationToken ct)
    {
        _logger.LogDebug("Getting data view for {dataViewId}", dataViewId);
        var response = await _httpClient.GetAsync($"/api/data_views/data_view/{dataViewId}", ct);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task CreateDataView(string dataViewId, string indexName, CancellationToken ct)
    {
        _logger.LogDebug("Creating data view for {dataViewId}", dataViewId);
        var dataView = new
        {
            data_view = new
            {
                id = dataViewId,
                title = indexName,
                name = "Logs Data View",
                timeFieldName = "timeStamp"
            }
        };
        await _httpClient.PostAsync("/api/data_views/data_view", JsonContent.Create(dataView), ct);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}