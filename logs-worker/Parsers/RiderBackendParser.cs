using System.Text.RegularExpressions;
using System.Threading.Channels;
using JetBrains.Annotations;

namespace logs_worker.Parsers;

[UsedImplicitly]
public class RiderBackendParser : IFileParser
{
    private readonly ChannelWriter<Log> _writer;
    private readonly ILogger<RiderBackendParser> _logger;

    private const string FileNamePattern = @"\d+.backend.log";

    private const string Pattern =
        @"^(?<time>.*)\s*\|(?<level>[A-Z])\|\s*(?<source>.*)\s*\|\s*(?<thread>.*)\s*\|\s*(?<message>.*)\s*$";

    private readonly Regex _fileNameRegex = new(FileNamePattern);
    private readonly Regex _regex = new(Pattern);

    public RiderBackendParser(SeqExporter seqExporter, ILogger<RiderBackendParser> logger)
    {
        _writer = seqExporter.GetWriter();
        _logger = logger;
    }

    public bool IsApplicable(string filename) => _fileNameRegex.IsMatch(filename);

    public async Task ParseAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct)
    {
        var creationDate = file.CreationTime;
        var errorFilePath = errorDirectory.FullName + "/" + file.Name;
        await using var errorWriter = File.CreateText(errorFilePath);

        var parsedLogs = 0;
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
                if (currentLog?.Message != null)
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
                await _writer.WriteAsync(currentLog, ct);
            }

            currentLog = new Log();
            parsedLogs++;
            UpdateLogByMatch(currentLog, match, creationDate);
        }

        if (currentLog is not null)
        {
            await _writer.WriteAsync(currentLog, ct);
        }

        _logger.LogInformation("{LogCount} lines are parsed from the file {FileName}", parsedLogs, file.Name);
    }

    private void UpdateLogByMatch(Log log, Match match, DateTime creationDate)
    {
        for (var i = 1; i < match.Groups.Count; i++)
        {
            var group = match.Groups[i];
            UpdateLogByGroup(log, group, creationDate);
        }
    }

    private void UpdateLogByGroup(Log log, Group group, DateTime creationDate)
    {
        switch (group.Name)
        {
            case "time":
                if (TimeOnly.TryParse(group.Value, out var time))
                {
                    log.TimeStamp = creationDate.Date + time.ToTimeSpan();
                }
                else
                {
                    _logger.LogWarning("Cannot parse time {time}", group.Value);
                }

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
            case "thread":
                log.Thread = group.Value.Trim();
                break;
            case "message":
                log.Message = group.Value.Trim();
                break;
            default:
                return;
        }
    }

    private static LogLevel? ParseLogLevel(string level) => level switch
    {
        "T" => LogLevel.Trace,
        "V" => LogLevel.Debug,
        "I" => LogLevel.Information,
        "W" => LogLevel.Warning,
        "E" => LogLevel.Error,
        _ => null
    };
}