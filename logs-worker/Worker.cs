using logs_worker.HttpClients;

namespace logs_worker;

public class Worker : BackgroundService
{
    public const string LogIndexName = "my-logs";
    private const string LogDataViewId = "my-log-data-view";
    private const string DefaultLogDirectory = "/logs-folder";
    private const string DefaultErrorDirectory = "/logs-folder/error";

    private readonly ElasticSearchClient _elasticSearchClient;
    private readonly KibanaClient _kibanaClient;
    private readonly IEnumerable<IFileExporter> _exporters;

    private readonly ILogger<Worker> _logger;

    public Worker(
        ElasticSearchClient elasticSearchClient,
        KibanaClient kibanaClient,
        IEnumerable<IFileExporter> exporters,
        ILogger<Worker> logger
    )
    {
        _elasticSearchClient = elasticSearchClient;
        _kibanaClient = kibanaClient;
        _exporters = exporters;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);

        _logger.LogInformation("Start monitoring folder");

        var directory = new DirectoryInfo(DefaultLogDirectory);
        var errorDirectory = new DirectoryInfo(DefaultErrorDirectory);
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
            await ProcessFileAsync(file, errorDirectory, stoppingToken);
        }
        
        _logger.LogInformation("Exporting finished");
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await _elasticSearchClient.CreateIndex(LogIndexName, ct);

        var isDataViewExists = await _kibanaClient.IsDataViewExists(LogDataViewId, ct);
        if (!isDataViewExists)
        {
            await _kibanaClient.CreateDataView(LogDataViewId, LogIndexName, ct);
        }

        if (!Directory.Exists(DefaultErrorDirectory))
        {
            Directory.CreateDirectory(DefaultErrorDirectory);
        }
    }

    private async Task ProcessFileAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct)
    {
        foreach (var exporter in _exporters)
        {
            if (!exporter.IsApplicable(file.Name)) continue;

            _logger.LogInformation("Exporting file: {filename}", file.Name);
            await exporter.ExportAsync(file, errorDirectory, ct);
            _logger.LogInformation("Exporting file finished: {filename}", file.Name);

            break;
        }
    }
}