using System.Net.Http.Json;
using Elastic.Clients.Elasticsearch;

namespace logs_worker;

public class Worker : BackgroundService
{
    private const string LogIndexName = "my-logs";

    private readonly ElasticsearchClient _elasticsearchClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Worker> _logger;

    public Worker(ElasticsearchClient elasticsearchClient, IHttpClientFactory httpClientFactory, ILogger<Worker> logger)
    {
        _elasticsearchClient = elasticsearchClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);

        _logger.LogInformation("Start monitoring folder");

        var directory = new DirectoryInfo("/logs-folder");
        while (true)
        {
            if (directory.EnumerateFiles().Any())
            {
                break;
            }

            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("Log files are found");

        foreach (var file in directory.GetFiles())
        {
            await ProcessFileAsync(file);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellation)
    {
        _logger.LogInformation("Creating index {index}", LogIndexName);
        using var elasticsearchClient = _httpClientFactory.CreateClient("elasticsearch");
        await elasticsearchClient.PutAsync($"/{LogIndexName}", JsonContent.Create(new { }), cancellation);

        _logger.LogInformation("Creating data view for {index}", LogIndexName);
        using var kibanaClient = _httpClientFactory.CreateClient("kibana");
        var dataView = new
        {
            data_view = new
            {
                title = LogIndexName,
                name = "Logs Data View"
            }
        };
        await kibanaClient.PostAsync("/api/data_views/data_view", JsonContent.Create(dataView), cancellation);
    }

    private async Task ProcessFileAsync(FileInfo file)
    {
        var log = new { Title = "My-log" };
        var response = await _elasticsearchClient.IndexAsync(log, request => request.Index(LogIndexName));
        if (response.IsValidResponse)
        {
            _logger.LogInformation($"Index document with ID {response.Id} succeeded.");
        }
    }
}