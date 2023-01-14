namespace logs_worker;

public interface IFileExporter
{
    public bool IsApplicable(string filename);
    public Task ExportAsync(FileInfo file, DirectoryInfo errorDirectory, CancellationToken ct);
}