using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using DriveInsight.Models;

namespace DriveInsight.Services;

public static class ElevatedDeepScanRunner
{
    public const string CommandName = "--deep-scan";
    public const string ChildrenCommandName = "--deep-children";
    public const string HelperCommandName = "--deep-helper";

    public static bool IsDeepScanCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeepChildrenCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], ChildrenCommandName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeepHelperCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], HelperCommandName, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (IsDeepHelperCommand(args))
        {
            return await RunHelperAsync(args);
        }

        if (IsDeepChildrenCommand(args))
        {
            return await RunDeepChildrenAsync(args);
        }

        if (args.Length < 3)
        {
            return 2;
        }

        var driveName = args[1];
        var outputPath = args[2];
        var drive = DriveInfo.GetDrives()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, driveName, StringComparison.OrdinalIgnoreCase));

        if (drive is null || !drive.IsReady)
        {
            return 3;
        }

        var scanner = new DriveScanner();
        var scan = await scanner.GetTopFolderScanAsync(
            drive.RootDirectory.FullName,
            top: 20,
            mode: StorageScanMode.Deep);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(scan.TopFolders));
        return 0;
    }

    private static async Task<int> RunDeepChildrenAsync(string[] args)
    {
        if (args.Length < 3)
        {
            return 2;
        }

        var folderPath = args[1];
        var outputPath = args[2];
        if (!Directory.Exists(folderPath))
        {
            return 4;
        }

        var scanner = new DriveScanner();
        var children = await scanner.GetImmediateChildrenAsync(folderPath, StorageScanMode.Deep);
        var folderPaths = children
            .Where(child => child.IsFolder)
            .Select(child => child.FullPath)
            .ToList();

        if (folderPaths.Count > 0)
        {
            var folderSizes = await scanner.GetFolderSizesAsync(folderPaths, StorageScanMode.Deep);
            children = children
                .Select(child => child.IsFolder && folderSizes.TryGetValue(child.FullPath, out var bytes)
                    ? new FileSystemEntry
                    {
                        Name = child.Name,
                        FullPath = child.FullPath,
                        IsFolder = child.IsFolder,
                        Bytes = bytes
                    }
                    : child)
                .ToList();
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(children));
        return 0;
    }

    private static async Task<int> RunHelperAsync(string[] args)
    {
        if (args.Length < 2)
        {
            return 2;
        }

        await using var pipe = new NamedPipeClientStream(
            ".",
            args[1],
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(30000);

        using var reader = new StreamReader(pipe);
        await using var writer = new StreamWriter(pipe) { AutoFlush = true };

        while (true)
        {
            var requestJson = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return 0;
            }

            var request = JsonSerializer.Deserialize<ElevatedScanRequest>(requestJson);
            if (request is null)
            {
                await WriteResponseAsync(writer, new ElevatedScanResponse
                {
                    Success = false,
                    Error = "Invalid request."
                });
                continue;
            }

            if (request.Command == ElevatedScanCommand.Shutdown)
            {
                return 0;
            }

            await WriteResponseAsync(writer, await HandleRequestAsync(request));
        }
    }

    private static async Task<ElevatedScanResponse> HandleRequestAsync(ElevatedScanRequest request)
    {
        try
        {
            if (request.Command == ElevatedScanCommand.ScanDrive)
            {
                var scan = await ScanDriveAsync(request.TargetPath);
                return new ElevatedScanResponse
                {
                    Success = true,
                    TopFolders = scan.TopFolders,
                    RootBytes = scan.RootBytes
                };
            }

            return request.Command switch
            {
                ElevatedScanCommand.LoadChildren => new ElevatedScanResponse
                {
                    Success = true,
                    Children = await LoadChildrenAsync(request.TargetPath)
                },
                _ => new ElevatedScanResponse
                {
                    Success = false,
                    Error = "Unsupported request."
                }
            };
        }
        catch (Exception ex)
        {
            return new ElevatedScanResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task WriteResponseAsync(StreamWriter writer, ElevatedScanResponse response)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }

    private static async Task<DriveScanner.TopFolderScanResult> ScanDriveAsync(string driveName)
    {
        var drive = DriveInfo.GetDrives()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, driveName, StringComparison.OrdinalIgnoreCase));

        if (drive is null || !drive.IsReady)
        {
            throw new InvalidOperationException("Drive is not ready.");
        }

        var scanner = new DriveScanner();
        return await scanner.GetTopFolderScanAsync(
            drive.RootDirectory.FullName,
            top: 20,
            mode: StorageScanMode.Deep);
    }

    private static async Task<List<FileSystemEntry>> LoadChildrenAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException("Folder does not exist.");
        }

        var scanner = new DriveScanner();
        var children = await scanner.GetImmediateChildrenAsync(folderPath, StorageScanMode.Deep);
        var folderPaths = children
            .Where(child => child.IsFolder)
            .Select(child => child.FullPath)
            .ToList();

        if (folderPaths.Count == 0)
        {
            return children;
        }

        var folderSizes = await scanner.GetFolderSizesAsync(folderPaths, StorageScanMode.Deep);
        return children
            .Select(child => child.IsFolder && folderSizes.TryGetValue(child.FullPath, out var bytes)
                ? new FileSystemEntry
                {
                    Name = child.Name,
                    FullPath = child.FullPath,
                    IsFolder = child.IsFolder,
                    Bytes = bytes
                }
                : child)
            .ToList();
    }
}
