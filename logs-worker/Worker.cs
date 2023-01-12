using System.Net.Http.Json;

namespace logs_worker;

public class Worker : BackgroundService
{
    public const string LogIndexName = "my-logs";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<IFileExporter> _exporters;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IHttpClientFactory httpClientFactory, 
        IEnumerable<IFileExporter> exporters,
        ILogger<Worker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _exporters = exporters;
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
            await ProcessFileAsync(file, stoppingToken);
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

    private async Task ProcessFileAsync(FileInfo file, CancellationToken cancellation)
    {
        foreach (var exporter in _exporters)
        {
            if (!exporter.IsApplicable(file.Name)) continue;

            _logger.LogDebug("Exporting file: {filename}", file.Name);
            await exporter.ExportAsync(file, cancellation);
            break;
        }
    }
}