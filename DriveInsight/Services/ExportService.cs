using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DriveInsight.Utilities;
using DriveInsight.ViewModels;

namespace DriveInsight.Services;

public interface IExportService
{
    Task ExportDriveInsightCsvAsync(
        DashboardPaneViewModel dashboard,
        DrivesPaneViewModel drives,
        StorageBreakdownPaneViewModel storage,
        CancellationToken ct = default);
}

public sealed class ExportService(Window owner) : IExportService
{
    private static readonly FilePickerFileType CsvFileType = new("CSV files")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv"]
    };

    public async Task ExportDriveInsightCsvAsync(
        DashboardPaneViewModel dashboard,
        DrivesPaneViewModel drives,
        StorageBreakdownPaneViewModel storage,
        CancellationToken ct = default)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            SuggestedFileName = $"driveinsight-export-{DateTime.Now:dd-MM-yyyy-HHmm}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [CsvFileType]
        });

        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await WriteCsvAsync(stream, BuildRows(dashboard, drives, storage), ct);
    }

    public static async Task WriteCsvAsync(Stream stream, IEnumerable<string[]> rows, CancellationToken ct = default)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: false);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", row.Select(Escape)));
        }
    }

    private static List<string[]> BuildRows(
        DashboardPaneViewModel dashboard,
        DrivesPaneViewModel drives,
        StorageBreakdownPaneViewModel storage)
    {
        var rows = new List<string[]> { HeaderRow(), SystemSummaryRow(dashboard) };

        rows.AddRange(dashboard.DriveBreakdowns.Select(DriveCapacityRow));
        rows.AddRange(dashboard.LargestFiles.Select(LargestFileRow));
        rows.AddRange(storage.BreakdownItems.Select(item => StorageBreakdownRow(item, storage)));
        rows.AddRange(FlattenFolderRows(drives.FolderRows)
            .Where(row => !row.IsPlaceholder)
            .Select(row => ScannedDriveItemRow(row, drives)));

        return rows;
    }

    private static string[] HeaderRow() =>
    [
        "Section", "Name", "Path", "Drive", "Type", "Size", "Size Bytes", "Used",
        "Used Bytes", "Total", "Total Bytes", "Available", "Available Bytes",
        "Percent", "Category", "Notes"
    ];

    private static string[] SystemSummaryRow(DashboardPaneViewModel dashboard) =>
    [
        "System Summary", "All ready drives", string.Empty, string.Empty, "Capacity", string.Empty, string.Empty,
        dashboard.TotalUsedText, Invariant(dashboard.TotalUsedBytes), dashboard.TotalCapacityText,
        Invariant(dashboard.TotalCapacityBytes), string.Empty, string.Empty,
        Invariant(dashboard.TotalUtilizationPercent), string.Empty, "Dashboard totals"
    ];

    private static string[] DriveCapacityRow(DriveCapacityCardViewModel drive) =>
    [
        "Drive Capacity", drive.DriveName, string.Empty, drive.DriveName, "Drive", string.Empty, string.Empty,
        StorageFormatter.Format(drive.UsedBytes), Invariant(drive.UsedBytes),
        StorageFormatter.Format(drive.TotalBytes), Invariant(drive.TotalBytes),
        StorageFormatter.Format(drive.TotalBytes - drive.UsedBytes), Invariant(drive.TotalBytes - drive.UsedBytes),
        Invariant(drive.UsedPercent), string.Empty, string.Empty
    ];

    private static string[] LargestFileRow(LargestFileViewModel file) =>
    [
        "Largest Files", file.Name, file.FilePath, ResolveDriveName(file.FilePath), "File",
        file.SizeText, Invariant(file.SizeBytes), string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, file.Category, string.Empty
    ];

    private static string[] StorageBreakdownRow(StorageBreakdownItemViewModel item, StorageBreakdownPaneViewModel storage) =>
    [
        "Storage Breakdown", item.Name, item.FullPath, storage.SelectedDrive?.Name.TrimEnd('\\') ?? ResolveDriveName(item.FullPath),
        "Folder", item.SizeText, Invariant(item.Bytes), string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, Invariant(item.Percent), string.Empty,
        "Share of scanned storage breakdown"
    ];

    private static string[] ScannedDriveItemRow(DriveFolderRowViewModel row, DrivesPaneViewModel drives) =>
    [
        "Scanned Drive Items", row.Name, row.FullPath, drives.SelectedDrive?.Name.TrimEnd('\\') ?? ResolveDriveName(row.FullPath),
        row.IsFolder ? "Folder" : "File", row.SizeText, Invariant(row.SizeBytes), string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, Invariant(row.UsagePercent), string.Empty,
        row.HasLoadedChildren ? "Expanded in app" : string.Empty
    ];

    private static IEnumerable<DriveFolderRowViewModel> FlattenFolderRows(IEnumerable<DriveFolderRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            yield return row;

            foreach (var child in FlattenFolderRows(row.Children))
            {
                yield return child;
            }
        }
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string ResolveDriveName(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetPathRoot(path)?.TrimEnd('\\') ?? string.Empty;
    }

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed class NullExportService : IExportService
{
    public Task ExportDriveInsightCsvAsync(
        DashboardPaneViewModel dashboard,
        DrivesPaneViewModel drives,
        StorageBreakdownPaneViewModel storage,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
