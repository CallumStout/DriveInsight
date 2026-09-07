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
- Supports an optional **Deep Scan** in the **Drives** pane. Deep Scan prompts for administrator access once, then reuses an elevated helper session while expanding folders that need administrator access.
- Displays results in a hierarchical **TreeDataGrid** with columns:
  - `FOLDER NAME`
  - `LOCATION`
  - `SIZE`
  - `USAGE SHARE`
- Supports expanding folders to lazily load child folders/files. Rows created by Deep Scan continue using the elevated helper for nested expansion.
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

- `Program.cs` - App entry point, Avalonia setup, and elevated helper command routing.
- `Views/` - Avalonia XAML UI, dashboard/drives/storage panes, and cleanup confirmation/review dialogs.
- `ViewModels/` - MVVM logic for panes, cards, smart insights, and cleanup candidates.
- `Services/DriveScanner.cs` - Drive, file, and folder scanning logic with bounded parallel traversal.
- `Services/ElevatedDeepScanRunner.cs` - Elevated helper entry points for deep drive scans and nested deep expansion.
- `Services/ElevatedDeepScanSession.cs` - Named-pipe session used by the normal app to reuse one elevated helper process after the initial UAC prompt.
- `Services/StorageScanMode.cs` - Normal vs deep scan mode selection for elevated helper routing.
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
3. The dashboard uses a dedicated largest-files scan with independent drive queues and a shared top-five cutoff. It displays interim results while scanning, then builds smart insights. Storage Breakdown waits until its pane is opened, so it does not compete with the dashboard at startup.
4. Clicking **Review cleanup** scans the constrained drive for low-risk cleanup targets and review-only candidates, then shows a checklist dialog.
5. Clicking **Remove** on the `Windows.old` insight asks for confirmation before deletion.
6. In the **Drives** pane, clicking **Run Scan** calls `GetTopFoldersAsync(rootPath)` in normal scan mode.
7. The scanner reads metadata in batches with a bounded number of workers, retains every descendant folder total, orders by descending bytes, and returns top N folders (default: 20).
8. Clicking **Deep Scan** starts an elevated helper process through UAC, connects to it over a named pipe, and requests a deep top-folder scan for the selected drive.
9. Scans do not intentionally exclude files by name, system attribute, or protected-path segment. Directory junctions, symbolic links and unknown reparse tags are skipped to avoid loops and duplicate traversal. On supported Windows file systems, non-link reparse directories (including cloud folder markers) are included. The elevated helper remains alive for the deep scan session so expanding deep-scanned child rows does not prompt for UAC every time.
10. Expanding a normal row reads immediate children and reuses their cached recursive totals. Expanding a deep-scanned row uses the same cache inside the persistent elevated helper.
11. In the **Storage Breakdown** pane, selecting a drive reuses a completed result from the last two minutes, or calls `GetStorageBreakdownScanAsync(...)` for a fresh summary. Refresh always bypasses that cache.
12. The storage breakdown uses a dedicated top-level summary traversal, adds an **Other scanned files** bucket, and adds **System / Protected** for used drive space that cannot be attributed to accessible scanned files. Its centre shows the total represented by the chart, including that remainder.
13. Clicking the header export button opens a save-file picker and writes a CSV named with UK date ordering, such as `driveinsight-export-19-05-2026-1430.csv`.
14. Clicking the header theme button switches Avalonia's requested theme variant and saves the choice to `%LocalAppData%\DriveInsight\theme.txt`.

## Notes / Known Limitations

- Deep scans can still take time on very large drives and require approving a Windows UAC prompt.
- The elevated helper is reused for nested expansion after a Deep Scan. Starting a normal scan or refreshing drives ends that helper session.
- Some directories/files may still be missing if Windows denies enumeration or size reads.
- Junctions and directory symbolic links are skipped; cloud folder metadata is included where the file-system driver supports it.
- Folder sizes are logical file lengths, not physical allocation. Hard links may count more than once; sparse, compressed and cloud files can have logical sizes larger than their disk usage. Alternate data streams and filesystem metadata are not included.
- CSV exports contain the data currently loaded in the app. Scanned drive rows are only included after the user runs a drive scan.
- Storage Breakdown is folder-based rather than semantic-category-based. It shows where space is used instead of guessing whether folders are games, apps, media, or backups.
- **System / Protected** and **Unattributed system space** are calculated from drive-used bytes minus scanner-visible bytes, so they can include reserved storage, restore data, NTFS metadata, locked files, and anything else Windows reports as used but does not expose as enumerable files.
- Cleanup actions may require administrator permission for protected locations.
- Cleanup candidates marked `Review` are intentionally unchecked by default because DriveInsight cannot know whether user/project files are safe to remove.

## Future Improvements

- Add cancellation support in long-running dashboard cleanup scans.
- Add live progress reporting.
- Add richer cleanup error reporting when deletion fails.

## Scanner performance and coverage

Windows scans use `GetFileInformationByHandleEx` with reusable 64 KiB buffers. File sizes and attributes come from directory records, without opening each file or allocating `FileInfo` objects. Other platforms and unsupported Windows directory-information classes fall back to managed enumeration. Both paths include hidden and system entries.

A single traversal builds all descendant folder totals. Folder expansion then reads only the immediate directory and looks up those totals. The startup largest-files pass has its own traversal: it retains only a bounded global ranking and pending directory paths, avoiding folder-tree aggregation and cache allocation. Normal and deep snapshots are isolated; cancelled scans do not publish partial snapshots. Run Scan, Deep Scan, and Storage Breakdown refresh request fresh results. Cached totals otherwise represent the last scan, so refresh after filesystem changes.

The elevated helper explicitly enables `SeBackupPrivilege` and opens directories with backup semantics. UAC alone does not enable that privilege or make ordinary directory enumeration bypass ACLs. If Windows policy does not grant the privilege, scanning continues with the access available to the helper. No file ownership or permissions are changed by the scanner. See Microsoft's [backup access documentation](https://learn.microsoft.com/en-us/windows/win32/fileio/file-security-and-access-rights) and [directory metadata API](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex).

Elevation cannot guarantee complete attribution: locked/offline volumes, file-system driver restrictions, metadata and alternate streams remain outside this traversal. The result reports file count, elapsed time, unreadable directories and skipped directory links. These are live scans, not atomic filesystem snapshots.

A local C: comparison on 7 September 2026 measured approximately 734,000 files in 2.5–3.1 seconds with the revised scanner. Expanding the three largest folders took 1–4 ms, versus 2.17–2.30 seconds previously. Warm full-scan times were similar (old: 2.46 s; revised: 2.53 s), while the revised scan counted about 51 GiB more logical file data. The old scanner's first run was 11.32 s; OS caching and different coverage mean that is not a controlled speedup ratio. Timings depend on disk speed, cache state, entry count, and file-system providers; seconds are not guaranteed on every drive. This measurement used a normal user token, not UAC elevation.

## Scanner validation

The standalone integration harness needs only the .NET SDK:

```powershell
dotnet run --project DriveInsight.ScannerTests -c Release -- --benchmark --backup-check
```

It checks aggregation, hidden/system files, Unicode names, native and managed enumeration, multi-buffer directories, long paths, junction cycles, top-file selection, mode isolation, cache refresh, overlapping paths, concurrent callers, and cancellation. The optional benchmark creates 20,000 files in 1,011 directories. `--backup-check` creates an ACL-restricted temporary fixture and restores its permissions afterward. It verifies denied-access reporting without elevation; in an already elevated terminal it also verifies that backup access can enumerate the fixture. It does not request UAC itself. The temporary fixture is removed after the run.

### Startup largest-items scan

The dashboard scan uses depth-first directory queues, up to eight metadata workers per drive (up to four drives at once), and a shared minimum-size cutoff. Files below the current top-five cutoff do not allocate result objects or enter the ranking lock. It does not construct recursive folder totals. Interim rankings are published at most once every 150 ms and labelled as results found so far; the scan indicator clears only when traversal finishes. Storage Breakdown starts on activation and cancels when hidden.

A warm-filesystem-cache comparison on this machine across C: and D: measured 2.35–2.38 seconds for final results, versus 2.96–3.11 seconds for the previous combined ranking/folder-total pass. First interim results arrived in 0.15 seconds. Managed allocations fell from about 239 MiB to 165 MiB. Both passes returned the same five file paths and sizes. These are scan timings, not total process/window startup times or a guarantee for cold disks and network drives.

### Storage Breakdown scan

Storage Breakdown uses a separate summary traversal with one accumulator per top-level folder and a depth-first queue. It reads every accessible descendant but does not retain a complete folder tree. Root files and folders outside the top eight go into Other scanned files; the positive difference from drive-used bytes goes into System / Protected. Unreadable-directory and skipped-link counts accompany the result and appear in the pane status.

Completed results are kept separately for each drive for two minutes and labelled with their scan time when reused. Switching drives clears the previous chart before loading the selected drive. Returning to a recently scanned drive restores its result without filesystem traversal. Refresh invalidates the selected drive's saved result and always scans again. A request identity, selected-drive check and cancellation token prevent delayed or cancelled operations from replacing newer results. Each request owns its cancellation source until its worker has stopped. Failed and cancelled requests are not cached. Chart transitions are disabled so a drive switch does not animate the previous drive's data into the new result.

A warm-filesystem-cache comparison measured C: at 2.17–2.23 seconds (previously 2.71–2.72 seconds), with managed allocations reduced from 206 MiB to 147 MiB. D: took 0.25–0.26 seconds (previously 0.30–0.33 seconds), with allocations reduced from 37 MiB to 26 MiB. These compare the previous full-folder-tree implementation with the summary implementation on this machine; cold storage and other hardware may differ.

Run the deterministic pane lifecycle tests with:

```powershell
dotnet run --project DriveInsight.ViewModelTests -c Release
```

These use a controlled scan provider to complete requests out of order and verify selection changes, cache expiry, forced refresh, failure/retry, hiding/reopening, missing drives, and coverage reporting. The scanner harness separately checks that the summary matches recursive folder totals and reconciles root files, other folders and unattributed space.
