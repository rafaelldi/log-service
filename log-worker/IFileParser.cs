namespace logs_worker;

public interface IFileParser
{
    public bool IsApplicable(string filename);
    public Task ParseAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct);
}