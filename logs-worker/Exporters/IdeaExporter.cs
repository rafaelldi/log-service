using System.Globalization;
using System.Text.RegularExpressions;
using Elastic.Clients.Elasticsearch;

namespace logs_worker.Exporters;

public class IdeaExporter : IFileExporter
{
    private readonly ElasticsearchClient _elasticsearchClient;
    private readonly ILogger<IdeaExporter> _logger;
    private const string Pattern = @"^(?<time>[^\[]*)\s+\[\s*(?<duration>\d+)\]\s+(?<level>[A-Z]+)\s+\-\s*(?<source>.*?)\s*-(?<message>.*)$";
    private readonly Regex _regex = new(Pattern);

    public IdeaExporter(ElasticsearchClient elasticsearchClient, ILogger<IdeaExporter> logger)
    {
        _elasticsearchClient = elasticsearchClient;
        _logger = logger;
    }

    public bool IsApplicable(string filename) => filename == "idea.log";

    public async Task ExportAsync(FileInfo file, CancellationToken cancellationToken)
    {
        await foreach (var line in File.ReadLinesAsync(file.FullName, cancellationToken))
        {
            var match = _regex.Match(line);
            if (!match.Success)
            {
                _logger.LogWarning("Cannot parse line: {line}", line);
                continue;
            }

            if (match.Groups.Count != 6)
            {
                _logger.LogWarning("Parsed line doesn't contain 6 groups: {line}", line);
                continue;
            }

            await ExportLogAsync(match);
        }
    }

    private async Task ExportLogAsync(Match match)
    {
        var log = new Log();
        for (var i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            switch (group.Name)
            {
                case "time":
                    log.TimeStamp = DateTime.ParseExact(group.Value, "yyyy-MM-dd HH:mm:ss,FFF", CultureInfo.InvariantCulture);
                    break;
                case "duration":
                    log.Duration = group.Value.Trim();
                    break;
                case "level":
                    log.Level = ParseLogLevel(group.Value.Trim());
                    break;
                case "source":
                    log.Source = group.Value.Trim();
                    break;
                case "message":
                    log.Message = group.Value.Trim();
                    break;
                default:
                    continue;
            }
        }
        
        var response = await _elasticsearchClient.IndexAsync(log, request => request.Index(Worker.LogIndexName));
        if (!response.IsValidResponse)
        {
            _logger.LogWarning("Unable to push log: {response}", response.ToString());
        }
    }

    private static LogLevel? ParseLogLevel(string level) => level switch
    {
        "INFO" => LogLevel.Info,
        _ => null
    };
}