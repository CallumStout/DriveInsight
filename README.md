# DriveInsight

DriveInsight is a desktop app built with Avalonia that scans local drives, visualizes storage usage, surfaces large files, and provides smart cleanup suggestions.

## Features

- Shows a **Dashboard** with total system capacity, per-drive usage cards, largest files, and smart insights.
- Lists all ready drives on the machine in the **Drives** pane.
- Shows a drive overview card with:
  - used percentage (circular indicator)
  - total capacity
  - used space
  - available space
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

## Tech Stack

- .NET 10 (`net10.0`)
- [Avalonia UI](https://avaloniaui.net/) 11.3.x
- `Avalonia.Controls.TreeDataGrid` 11.0.2
- CommunityToolkit.Mvvm

## Project Structure

- `Program.cs` - App entry point and Avalonia setup.
- `Views/` - Avalonia XAML UI, dashboard/drives panes, and cleanup confirmation/review dialogs.
- `ViewModels/` - MVVM logic for panes, cards, smart insights, and cleanup candidates.
- `Services/DriveScanner.cs` - Drive, file, and folder scanning logic.
- `Services/CleanupCandidateScannerService.cs` - Finds cleanup candidates for constrained drives.
- `Services/CleanupRemovalService.cs` - Removes selected cleanup candidates.
- `Services/IConfirmationDialogService.cs` and `Services/ICleanupReviewDialogService.cs` - UI dialog abstractions used by view models.
- `Utilities/StorageFormatter.cs` - Shared byte-size formatting helper.
- `Models/FolderStats.cs` - Top-level folder size model.
- `Controls/CircularProgress.cs` - Custom circular usage indicator for the drive overview.

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

## Notes / Known Limitations

- Deep scans can still take time on very large drives.
- Some directories/files may be skipped if access is denied.
- Reparse points/junctions are skipped to avoid recursion issues and inconsistent totals.
- Folder sizes are recursive estimates based on accessible files.
- Cleanup actions may require administrator permission for protected locations.
- Cleanup candidates marked `Review` are intentionally unchecked by default because DriveInsight cannot know whether user/project files are safe to remove.

## Future Improvements

- Add cancellation support in long-running dashboard cleanup scans.
- Add progress reporting and scan duration.
- Export results to CSV.
- Add unit tests for scanner behavior.
- Add richer cleanup error reporting when deletion fails.
