# DriveInsight

DriveInsight is a desktop app built with Avalonia that scans local drives, visualizes storage usage, surfaces large files, and provides smart cleanup suggestions.

## Features

- Shows a **Dashboard** with total system capacity, per-drive usage cards, largest files, and smart insights.
- Lists all ready drives on the machine in the **Drives** pane.
- Shows a drive overview card with:
  - used percentage (LiveCharts gauge)
  - total capacity
  - used space
  - available space
- Shows a **Storage Breakdown** pane with:
  - drive tabs
  - nested pie chart for selected-drive usage
  - top-level folders by size
  - other scanned files
  - system/protected space that is used by the drive but not visible to the scanner
  - scrollable legend with folder paths, percentages, and formatted sizes
- Scans the selected drive and loads top-level folders by size.
- Displays results in a hierarchical **TreeDataGrid** with columns:
  - `FOLDER NAME`
  - `LOCATION`
  - `SIZE`
  - `USAGE SHARE`
- Supports expanding folders to lazily load child folders/files.
- Smart insights include:
  - constrained-drive cleanup review
  - `Windows.old` removal with confirmation
  - largest-file **Open location** action
- Cleanup review dialog lists removable candidates with checkboxes, size, path, reason, and risk level.
- Developer-oriented cleanup detects common build/cache folders inside Git repositories, such as `bin`, `obj`, `dist`, `build`, `target`, `.vs`, `.next`, and `coverage`.
- Exports currently loaded results to CSV from the header export button. The export includes dashboard totals, drive capacity rows, largest files, storage breakdown rows, and scanned drive items once a drive scan has been run.
- Supports light and dark themes from the header sun/moon toggle, with the selected theme saved between app launches.

## Tech Stack

- .NET 10 (`net10.0`)
- [Avalonia UI](https://avaloniaui.net/) 11.3.x
- `Avalonia.Controls.TreeDataGrid` 11.0.2
- `LiveChartsCore.SkiaSharpView.Avalonia` 2.0.1
- CommunityToolkit.Mvvm

## Project Structure

- `Program.cs` - App entry point and Avalonia setup.
- `Views/` - Avalonia XAML UI, dashboard/drives/storage panes, and cleanup confirmation/review dialogs.
- `ViewModels/` - MVVM logic for panes, cards, smart insights, and cleanup candidates.
- `Services/DriveScanner.cs` - Drive, file, and folder scanning logic.
- `Services/CleanupCandidateScannerService.cs` - Finds cleanup candidates for constrained drives.
- `Services/CleanupRemovalService.cs` - Removes selected cleanup candidates.
- `Services/ExportService.cs` - Builds DriveInsight CSV exports and opens the platform save-file picker.
- `Services/AppThemeService.cs` - Applies the saved light/dark theme and persists theme changes.
- `Services/IConfirmationDialogService.cs` and `Services/ICleanupReviewDialogService.cs` - UI dialog abstractions used by view models.
- `Services/ConfirmationDialogService.cs` and `Services/CleanupReviewDialogService.cs` - Avalonia dialog service implementations.
- `Services/StorageBreakdownItem.cs` - Storage breakdown item model for the top-folder pie chart.
- `Utilities/StorageFormatter.cs` - Shared byte-size formatting helper.
- `Models/FolderStats.cs` - Top-level folder size model.

## Requirements

- .NET 10 SDK installed
- Windows/macOS/Linux supported by Avalonia (drive scanning logic is currently Windows-centric due to `System.IO.DriveInfo` UX assumptions)

## Run Locally

```bash
dotnet restore
dotnet build
dotnet run
```

## How It Works

1. On startup, the dashboard loads ready drives via `DriveScanner.GetReadyDrives()`.
2. Dashboard capacity cards are calculated from `DriveInfo` (`TotalSize`, `AvailableFreeSpace`).
3. The dashboard scans for the largest files and builds smart insights from drive pressure, `Windows.old`, and largest-file results.
4. Clicking **Review cleanup** scans the constrained drive for low-risk cleanup targets and review-only candidates, then shows a checklist dialog.
5. Clicking **Remove** on the `Windows.old` insight asks for confirmation before deletion.
6. In the **Drives** pane, clicking **Run Scan** calls `GetTopFoldersAsync(rootPath)`.
7. The scanner computes folder sizes recursively (access-safe traversal), orders by descending bytes, and returns top N folders (default: 20).
8. Expanding a row lazily loads immediate children and computes their sizes for nested display.
9. In the **Storage Breakdown** pane, selecting a drive calls `GetStorageBreakdownAsync(...)`.
10. The storage breakdown reuses top-folder scanning, adds an **Other scanned files** bucket, and adds **System / Protected** for used drive space that cannot be attributed to accessible scanned files.
11. Clicking the header export button opens a save-file picker and writes a CSV named with UK date ordering, such as `driveinsight-export-19-05-2026-1430.csv`.
12. Clicking the header theme button switches Avalonia's requested theme variant and saves the choice to `%LocalAppData%\DriveInsight\theme.txt`.

## Notes / Known Limitations

- Deep scans can still take time on very large drives.
- Some directories/files may be skipped if access is denied.
- Reparse points/junctions are skipped to avoid recursion issues and inconsistent totals.
- Folder sizes are recursive estimates based on accessible files.
- CSV exports contain the data currently loaded in the app. Scanned drive rows are only included after the user runs a drive scan.
- Storage Breakdown is folder-based rather than semantic-category-based. It shows where space is used instead of guessing whether folders are games, apps, media, or backups.
- **System / Protected** is calculated from drive-used bytes minus scanner-visible bytes, so it can include OS files, reserved storage, restore data, protected folders, and other inaccessible filesystem data.
- Cleanup actions may require administrator permission for protected locations.
- Cleanup candidates marked `Review` are intentionally unchecked by default because DriveInsight cannot know whether user/project files are safe to remove.

## Future Improvements

- Add cancellation support in long-running dashboard cleanup scans.
- Add progress reporting and scan duration.
- Add unit tests for scanner behavior.
- Add richer cleanup error reporting when deletion fails.
