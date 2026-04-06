namespace DriveInsight.Models;

public sealed class FileSystemEntry
{
    public string Name { get; init; } = "";

    public string FullPath { get; init; } = "";

    public long Bytes { get; init; }

    public bool IsFolder { get; init; }
}
