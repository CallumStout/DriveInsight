using System.Collections.ObjectModel;
using DriveInsight.Services;

namespace DriveInsight.Models;

public sealed class FileSystemNode
{
    public string Name { get; init; } = "";

    public string FullPath { get; init; } = "";

    public bool IsFolder { get; init; }

    public long Bytes { get; init; }

    public string SizeText { get; init; } = "";

    public bool HasLoadedChildren { get; set; }

    public bool IsPlaceholder { get; init; }

    public StorageScanMode ScanMode { get; init; } = StorageScanMode.Normal;

    public ObservableCollection<FileSystemNode> Children { get; } = [];
}
