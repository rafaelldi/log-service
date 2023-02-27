using System.Text;
using System.Threading.Channels;
using logs_worker.HttpClients;

namespace logs_worker;

public class SeqExporter
{
    private readonly SeqClient _seqClient;
    private readonly ILogger<SeqExporter> _logger;
    private readonly Channel<Log> _channel;

    public SeqExporter(SeqClient seqClient, ILogger<SeqExporter> logger)
    {
        _seqClient = seqClient;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Log>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelWriter<Log> GetWriter() => _channel.Writer;

    public async Task ExportAsync(DirectoryInfo errorDirectory, CancellationToken ct)
    {
        var errorFilePath = errorDirectory.FullName + "/exporter.log";
        await using var errorWriter = File.CreateText(errorFilePath);

        var sb = new StringBuilder();
        var logsCount = 0;
        var sentLogs = 0;

        await foreach (var log in _channel.Reader.ReadAllAsync(ct))
        {
            if (log.TimeStamp is null || log.Level is null)
            {
                await errorWriter.WriteLineAsync(log.ToString());
                continue;
            }

            logsCount++;

            AppendLogLine(
                sb,
                log.TimeStamp.Value,
                log.Level.Value,
                log.Exception,
                log.Duration,
                log.Source,
                log.Thread,
                log.Message
            );

            if (logsCount < 10) continue;

            var logs = sb.ToString();
            var result = await _seqClient.PostLogAsync(logs, ct);
            if (result != true)
            {
                await errorWriter.WriteLineAsync(logs);
            }

            sentLogs += logsCount;
            logsCount = 0;
            sb.Clear();
        }

        _logger.LogInformation("{LogCount} logs are sent to Seq", sentLogs);
    }

    private void AppendLogLine(
        StringBuilder sb,
        DateTime timeSpan,
        LogLevel level,
        string? exception,
        string? duration,
        string? source,
        string? thread,
        string? message)
    {
        sb.Append("{\"@t\":\"");
        sb.Append(timeSpan.ToString("O"));
        sb.Append('\"');

        sb.Append(",\"@l\":\"");
        sb.Append(level.ToString());
        sb.Append('\"');

        sb.Append(",\"@mt\":\"");
        if (duration != null) sb.Append("{Duration},");
        if (source != null) sb.Append("{Source},");
        if (thread != null) sb.Append("{Thread},");
        if (message != null) sb.Append("{Message}");
        sb.Append('\"');

        if (duration != null)
        {
            sb.Append(",\"Duration\":\"");
            AppendEscapedValue(sb, duration);
            sb.Append('\"');
        }

        if (source != null)
        {
            sb.Append(",\"Source\":\"");
            AppendEscapedValue(sb, source);
            sb.Append('\"');
        }

        if (thread != null)
        {
            sb.Append(",\"Thread\":\"");
            AppendEscapedValue(sb, thread);
            sb.Append('\"');
        }

        if (message != null)
        {
            sb.Append(",\"Message\":\"");
            AppendEscapedValue(sb, message);
            sb.Append('\"');
        }

        if (exception != null)
        {
            sb.Append(",\"@x\":\"");
            AppendEscapedValue(sb, exception);
            sb.Append('\"');
        }

        sb.Append('}');

        sb.AppendLine();
    }

    private void AppendEscapedValue(StringBuilder sb, string? value)
    {
        if (value is null) return;

        var cleanSegmentStart = 0;
        var anyEscaped = false;

        for (var i = 0; i < value.Length; ++i)
        {
            var c = value[i];
            if (c is < (char)32 or '\\' or '"')
            {
                anyEscaped = true;
                sb.Append(value.AsSpan(cleanSegmentStart, i - cleanSegmentStart));
                cleanSegmentStart = i + 1;

                switch (c)
                {
                    case '"':
                    {
                        sb.Append("\\\"");
                        break;
                    }
                    case '\\':
                    {
                        sb.Append("\\\\");
                        break;
                    }
                    case '\n':
                    {
                        sb.Append("\\n");
                        break;
                    }
                    case '\r':
                    {
                        sb.Append("\\r");
                        break;
                    }
                    case '\f':
                    {
                        sb.Append("\\f");
                        break;
                    }
                    case '\t':
                    {
                        sb.Append("\\t");
                        break;
                    }
                    default:
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("X4"));
                        break;
                    }
                }
            }
        }

        if (anyEscaped)
        {
            if (cleanSegmentStart != value.Length)
                sb.Append(value[cleanSegmentStart..]);
        }
        else
        {
            sb.Append(value);
        }
    }
}