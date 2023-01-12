namespace logs_worker;

public class Log
{
    public DateTime? TimeStamp { get; set; }
    public string? Duration { get; set; }
    public LogLevel? Level { get; set; }
    public string? Source { get; set; }
    public string? Message { get; set; }
}