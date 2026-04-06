# DriveInsight

DriveInsight is a desktop app built with Avalonia that scans a selected drive and shows the largest top-level folders by total size.

## Features

- Lists all ready drives on the machine.
- Lets you pick a drive and run a scan.
- Calculates folder size recursively for each top-level folder.
- Displays results in a sortable table (`Folder`, `Path`, `Bytes`).

## Tech Stack

- .NET 10 (`net10.0`)
- [Avalonia UI](https://avaloniaui.net/) 11.3.x
- CommunityToolkit.Mvvm

## Project Structure

- `Program.cs` - App entry point and Avalonia setup.
- `Views/` - Avalonia XAML UI (`App`, `MainWindow`).
- `ViewModels/` - MVVM logic (`MainWindowViewModel`, `ViewModelBase`).
- `Services/DriveScanner.cs` - Drive and folder scanning logic.
- `Models/FolderStats.cs` - Result model for folder size rows.

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
2. When you click **Scan Selected Drive**, it calls `GetTopFoldersAsync(rootPath)`.
3. The scanner enumerates top-level directories and sums file sizes recursively.
4. Results are ordered by descending bytes and the top N (default: 20) are shown.

## Notes / Known Limitations

- Deep scans can take time on large drives.
- Some directories/files may be skipped if access is denied.
- Size is shown as raw bytes (no human-readable formatter yet).
- Current implementation scans all files recursively under each top-level folder.

## Future Improvements

- Add cancellation support in the UI.
- Format sizes as KB/MB/GB.
- Add progress reporting and scan duration.
- Export results to CSV.
- Add unit tests for scanner behavior.
