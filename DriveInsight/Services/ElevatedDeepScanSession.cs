using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DriveInsight.Models;

namespace DriveInsight.Services;

public sealed class ElevatedDeepScanSession : IAsyncDisposable
{
    private readonly Process process;
    private readonly NamedPipeServerStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    private ElevatedDeepScanSession(Process process, NamedPipeServerStream pipe)
    {
        this.process = process;
        this.pipe = pipe;
        reader = new StreamReader(pipe);
        writer = new StreamWriter(pipe) { AutoFlush = true };
    }

    public static Task<ElevatedDeepScanSession?> StartAsync(string processPath) =>
        Task.Run(() => StartCoreAsync(processPath));

    private static async Task<ElevatedDeepScanSession?> StartCoreAsync(string processPath)
    {
        var pipeName = $"DriveInsightDeepScan-{Guid.NewGuid():N}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            startInfo.ArgumentList.Add(ElevatedDeepScanRunner.HelperCommandName);
            startInfo.ArgumentList.Add(pipeName);

            var process = Process.Start(startInfo);
            if (process is null)
            {
                await pipe.DisposeAsync();
                return null;
            }

            using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(connectionTimeout.Token).ConfigureAwait(false);
            return new ElevatedDeepScanSession(process, pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public async Task<DriveScanner.TopFolderScanResult> ScanDriveAsync(string driveName)
    {
        var response = await SendAsync(new ElevatedScanRequest
        {
            Command = ElevatedScanCommand.ScanDrive,
            TargetPath = driveName
        });

        return new DriveScanner.TopFolderScanResult(
            response.TopFolders ?? [],
            Math.Max(0, response.RootBytes), response.FileCount, response.UnreadableDirectories,
            response.SkippedLinks, response.ElapsedSeconds);
    }

    public async Task<List<FileSystemEntry>> LoadChildrenAsync(string folderPath)
    {
        var response = await SendAsync(new ElevatedScanRequest
        {
            Command = ElevatedScanCommand.LoadChildren,
            TargetPath = folderPath
        });

        return response.Children ?? [];
    }

    private Task<ElevatedScanResponse> SendAsync(ElevatedScanRequest request) =>
        Task.Run(() => SendCoreAsync(request));

    private async Task<ElevatedScanResponse> SendCoreAsync(ElevatedScanRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        await gate.WaitAsync();
        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var responseJson = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new IOException("The elevated scan helper disconnected.");
            }

            var response = JsonSerializer.Deserialize<ElevatedScanResponse>(responseJson)
                           ?? throw new IOException("The elevated scan helper returned an invalid response.");
            if (!response.Success)
            {
                throw new IOException(response.Error ?? "The elevated scan helper failed.");
            }

            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync() => new(Task.Run(DisposeCoreAsync));

    private async Task DisposeCoreAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            if (pipe.IsConnected)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new ElevatedScanRequest
                {
                    Command = ElevatedScanCommand.Shutdown
                }));
            }
        }
        catch
        {
        }

        try
        {
            reader.Dispose();
        }
        catch
        {
        }

        try
        {
            await writer.DisposeAsync();
        }
        catch
        {
        }

        try
        {
            await pipe.DisposeAsync();
        }
        catch
        {
        }

        gate.Dispose();

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        process.Dispose();
    }
}

public sealed class ElevatedScanRequest
{
    public required ElevatedScanCommand Command { get; init; }

    public string TargetPath { get; init; } = "";
}

public sealed class ElevatedScanResponse
{
    public required bool Success { get; init; }

    public string? Error { get; init; }

    public List<FolderStat>? TopFolders { get; init; }

    public long RootBytes { get; init; }

    public long FileCount { get; init; }
    public int UnreadableDirectories { get; init; }
    public int SkippedLinks { get; init; }
    public double ElapsedSeconds { get; init; }

    public List<FileSystemEntry>? Children { get; init; }
}

public enum ElevatedScanCommand
{
    ScanDrive,
    LoadChildren,
    Shutdown
}
