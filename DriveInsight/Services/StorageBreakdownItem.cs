namespace DriveInsight.Services;

public sealed class StorageBreakdownItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required long Bytes { get; init; }
}
