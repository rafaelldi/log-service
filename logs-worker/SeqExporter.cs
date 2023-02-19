using System.Text;
using System.Threading.Channels;
using logs_worker.HttpClients;

namespace logs_worker;

public class SeqExporter
{
    private readonly SeqClient _seqClient;
    private readonly Channel<Log> _channel;

    public SeqExporter(SeqClient seqClient)
    {
        _seqClient = seqClient;
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
                log.Message
            );

            if (logsCount < 10) continue;

            var logs = sb.ToString();
            var result = await _seqClient.PostLogAsync(logs, ct);
            if (result != true)
            {
                await errorWriter.WriteLineAsync(logs);
            }

            logsCount = 0;
            sb.Clear();
        }
    }

    private void AppendLogLine(
        StringBuilder sb,
        DateTime timeSpan,
        LogLevel level,
        string? exception,
        string? duration,
        string? source,
        string? message)
    {
        sb.Append("{\"@t\":\"");
        sb.Append(timeSpan.ToString("O"));
        sb.Append('\"');

        sb.Append(",\"@l\":\"");
        sb.Append(level.ToString());
        sb.Append('\"');

        sb.Append(",\"@mt\":\"");
        sb.Append("{Duration},{Source},{Message}");
        sb.Append('\"');

        sb.Append(",\"Duration\":\"");
        AppendEscapedValue(sb, duration);
        sb.Append('\"');

        sb.Append(",\"Source\":\"");
        AppendEscapedValue(sb, source);
        sb.Append('\"');

        sb.Append(",\"Message\":\"");
        AppendEscapedValue(sb, message);
        sb.Append('\"');

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