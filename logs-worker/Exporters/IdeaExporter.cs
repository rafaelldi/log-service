using System.Globalization;
using System.Text.RegularExpressions;
using Elastic.Clients.Elasticsearch;

namespace logs_worker.Exporters;

public class IdeaExporter : IFileExporter
{
    private const string DefaultFileName = "idea.log";
    private readonly ElasticsearchClient _elasticsearchClient;
    private readonly ILogger<IdeaExporter> _logger;

    private const string Pattern =
        @"^(?<time>[^\[]*)\s+\[\s*(?<duration>\d+)\]\s+(?<level>[A-Z]+)\s+\-\s*(?<source>.*?)\s*-(?<message>.*)$";

    private readonly Regex _regex = new(Pattern);

    public IdeaExporter(ElasticsearchClient elasticsearchClient, ILogger<IdeaExporter> logger)
    {
        _elasticsearchClient = elasticsearchClient;
        _logger = logger;
    }

    public bool IsApplicable(string filename) => filename == DefaultFileName;

    public async Task ExportAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct)
    {
        var errorFilePath = errorDirectory.FullName + "/" + DefaultFileName;
        await using var errorWriter = File.CreateText(errorFilePath);

        Log? currentLog = null;
        await foreach (var line in File.ReadLinesAsync(file.FullName, ct))
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var match = _regex.Match(line);
            if (!match.Success)
            {
                if (currentLog is not null)
                {
                    currentLog.Message += "\n" + line;
                }
                else
                {
                    await errorWriter.WriteLineAsync(line);
                }

                continue;
            }

            if (match.Groups.Count != 6)
            {
                await errorWriter.WriteLineAsync(line);
                continue;
            }

            if (currentLog is not null)
            {
                await ExportLogAsync(currentLog);
            }

            currentLog = new Log();
            UpdateLogByMatch(currentLog, match);
        }

        if (currentLog is not null)
        {
            await ExportLogAsync(currentLog);
        }
    }

    private void UpdateLogByMatch(Log log, Match match)
    {
        for (var i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            UpdateLogByGroup(log, group);
        }
    }

    private void UpdateLogByGroup(Log log, Group group)
    {
        switch (group.Name)
        {
            case "time":
                if (DateTime.TryParseExact(group.Value, "yyyy-MM-dd HH:mm:ss,FFF", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var time))
                {
                    log.TimeStamp = time;
                }
                else
                {
                    _logger.LogWarning("Cannot parse time {time}", group.Value);
                }

                break;
            case "duration":
                log.Duration = group.Value.Trim();
                break;
            case "level":
                var value = group.Value.Trim();
                var level = ParseLogLevel(value);
                if (level is null) _logger.LogWarning("Cannot parse log level {logLevel}", value);
                log.Level = level;
                break;
            case "source":
                log.Source = group.Value.Trim();
                break;
            case "message":
                log.Message = group.Value.Trim();
                break;
            default:
                return;
        }
    }

    private async Task ExportLogAsync(Log log)
    {
        var response = await _elasticsearchClient.IndexAsync(log, request => request.Index(Worker.LogIndexName));
        if (!response.IsValidResponse)
        {
            _logger.LogWarning("Unable to push log: {response}", response.ToString());
        }
    }

    private static LogLevel? ParseLogLevel(string level) => level switch
    {
        "INFO" => LogLevel.Info,
        "WARN" => LogLevel.Warn,
        "SEVERE" => LogLevel.Severe,
        _ => null
    };
}