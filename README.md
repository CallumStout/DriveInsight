# DriveInsight

DriveInsight is a desktop app built with Avalonia that scans a selected drive and visualizes storage usage with a top overview and a hierarchical folder explorer.

## Features

- Lists all ready drives on the machine as tabs in the **Disk Manager** header.
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

## Tech Stack

- .NET 10 (`net10.0`)
- [Avalonia UI](https://avaloniaui.net/) 11.3.x
- `Avalonia.Controls.TreeDataGrid` 11.0.2
- CommunityToolkit.Mvvm

## Project Structure

- `Program.cs` - App entry point and Avalonia setup.
- `Views/` - Avalonia XAML UI (`App`, `MainWindow`).
- `ViewModels/` - MVVM logic (`MainWindowViewModel`, `ViewModelBase`).
- `Services/DriveScanner.cs` - Drive and folder scanning logic.
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

1. On startup, `MainWindowViewModel` loads all ready drives via `DriveScanner.GetReadyDrives()`.
2. Selecting a drive updates the overview values from `DriveInfo` (`TotalSize`, `AvailableFreeSpace`).
3. Clicking **Run Scan** calls `GetTopFoldersAsync(rootPath)`.
4. The scanner computes folder sizes recursively (access-safe traversal), orders by descending bytes, and returns top N folders (default: 20).
5. Expanding a row lazily loads immediate children and computes their sizes for nested display.

## Notes / Known Limitations

- Deep scans can still take time on very large drives.
- Some directories/files may be skipped if access is denied.
- Reparse points/junctions are skipped to avoid recursion issues and inconsistent totals.
- Folder sizes are recursive estimates based on accessible files.

## Future Improvements

- Add cancellation support in the UI.
- Add progress reporting and scan duration.
- Export results to CSV.
- Add unit tests for scanner behavior.
