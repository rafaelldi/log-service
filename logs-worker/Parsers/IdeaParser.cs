using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using JetBrains.Annotations;

namespace logs_worker.Parsers;

[UsedImplicitly]
public class IdeaParser : IFileParser
{
    private readonly ChannelWriter<Log> _writer;
    private readonly ILogger<IdeaParser> _logger;

    private const string DefaultFileName = "idea.log";
    private const string FileNamePattern = @"idea\.\d+.log";

    private const string Pattern =
        @"^(?<time>[^\[]*)\s+\[\s*(?<duration>\d+)\]\s+(?<level>[A-Z]+)\s+\-\s*(?<source>.*?)\s*-(?<message>.*)$";

    private readonly Regex _fileNameRegex = new(FileNamePattern);
    private readonly Regex _regex = new(Pattern);

    public IdeaParser(SeqExporter seqExporter, ILogger<IdeaParser> logger)
    {
        _writer = seqExporter.GetWriter();
        _logger = logger;
    }

    public bool IsApplicable(string filename) => filename == DefaultFileName || _fileNameRegex.IsMatch(filename);

    public async Task ParseAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct)
    {
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
                    currentLog.Exception = currentLog.Message;
                    currentLog.Exception += "\n" + line;
                    currentLog.Message = null;
                }
                else if (currentLog?.Exception != null)
                {
                    currentLog.Exception += "\n" + line;
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
            UpdateLogByMatch(currentLog, match);
        }

        if (currentLog is not null)
        {
            await _writer.WriteAsync(currentLog, ct);
        }

        _logger.LogInformation("{LogCount} lines are parsed from the file {FileName}", parsedLogs, file.Name);
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

    private static LogLevel? ParseLogLevel(string level) => level switch
    {
        "FINER" => LogLevel.Trace,
        "FINE" => LogLevel.Debug,
        "INFO" => LogLevel.Information,
        "WARN" => LogLevel.Warning,
        "SEVERE" => LogLevel.Error,
        _ => null
    };
}