using System.Text.RegularExpressions;

namespace logs_worker;

public class Worker : BackgroundService
{
    private const string DefaultLogDirectory = "/logs-folder";
    private const string DefaultErrorDirectory = "/logs-folder/error";
    private const string TroubleshootingTxt = "/logs-folder/troubleshooting.txt";  

    private readonly IEnumerable<IFileParser> _parsers;
    private readonly SeqExporter _exporter;
    private readonly DateTimeProvider _dateTimeProvider;
    private readonly ILogger<Worker> _logger;

    public Worker(IEnumerable<IFileParser> parsers, SeqExporter exporter, DateTimeProvider dateTimeProvider,
        ILogger<Worker> logger)
    {
        _parsers = parsers;
        _exporter = exporter;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Initialize();

        _logger.LogInformation("Start monitoring folder {LogFolder}", DefaultLogDirectory);

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

    private async Task Initialize()
    {
        if (!Directory.Exists(DefaultErrorDirectory))
        {
            Directory.CreateDirectory(DefaultErrorDirectory);
        }

        if (File.Exists(TroubleshootingTxt))
        {
            using var sr = File.OpenText(TroubleshootingTxt);
            await sr.ReadLineAsync();
            var infoLine = await sr.ReadLineAsync() ?? string.Empty;
            var infoPattern = @"^Build\sversion:.+Build:\s#RD-[\d\.]+\s(?<date>.*)$";
            var infoRegex = new Regex(infoPattern);
            var match = infoRegex.Match(infoLine);
            if (match.Success &&
                match.Groups.TryGetValue("date", out var dateGroup) &&
                DateTime.TryParse(dateGroup.Value, out var date))
            {
                _dateTimeProvider.LogDateTime = date;
            }
        }
    }

    private IFileParser? GetParser(FileInfo file) =>
        _parsers.FirstOrDefault(exporter => exporter.IsApplicable(file.Name));
}