namespace DriveInsight.Models;

public sealed class FolderStat
{
    public string Name { get; init; } = "";

    public string FullPath { get; init; } = "";

    public long Bytes { get; init; }
}