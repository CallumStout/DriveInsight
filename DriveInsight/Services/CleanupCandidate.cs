namespace DriveInsight.Services;

public sealed class CleanupCandidate
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Reason { get; init; }
    public required string Risk { get; init; }
    public required long SizeBytes { get; init; }
    public required bool IsDirectory { get; init; }
    public required bool IsSelectedByDefault { get; init; }
}
