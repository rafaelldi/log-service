namespace logs_worker;

public record Log
{
    public DateTime? TimeStamp { get; set; }
    public LogLevel? Level { get; set; }
    public string? Duration { get; set; }
    public string? Source { get; set; }
    public string? Thread { get; set; }
    public string? Message { get; set; }
    public string? Exception { get; set; }
}