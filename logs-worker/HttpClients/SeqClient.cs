using System.Text;

namespace logs_worker.HttpClients;

public class SeqClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public SeqClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> PostLogAsync(string logs, CancellationToken ct)
    {
        var content = new StringContent(logs, Encoding.UTF8, "application/vnd.serilog.clef");
        var response = await _httpClient.PostAsync("/api/events/raw", content, ct);
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}