namespace logs_worker;

public class Worker : BackgroundService
{
    private const string DefaultLogDirectory = "/logs-folder";
    private const string DefaultErrorDirectory = "/logs-folder/error";

    private readonly IEnumerable<IFileParser> _parsers;
    private readonly SeqExporter _exporter;
    private readonly ILogger<Worker> _logger;

    public Worker(IEnumerable<IFileParser> parsers, SeqExporter exporter, ILogger<Worker> logger)
    {
        _parsers = parsers;
        _exporter = exporter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Initialize();

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

        var exporterTask = _exporter.ExportAsync(errorDirectory, stoppingToken);

        var parsersTasks = new List<Task>();
        foreach (var file in directory.GetFiles())
        {
            var exporter = GetParser(file);
            if (exporter is null) continue;
            var parsersTask = exporter.ParseAsync(file, errorDirectory, stoppingToken);
            parsersTasks.Add(parsersTask);
        }

        await Task.WhenAll(parsersTasks);
        
        _exporter.GetWriter().Complete();

        await exporterTask;

        _logger.LogInformation("Exporting finished");
    }

    private void Initialize()
    {
        if (!Directory.Exists(DefaultErrorDirectory))
        {
            Directory.CreateDirectory(DefaultErrorDirectory);
        }
    }

    private IFileParser? GetParser(FileInfo file) =>
        _parsers.FirstOrDefault(exporter => exporter.IsApplicable(file.Name));
}