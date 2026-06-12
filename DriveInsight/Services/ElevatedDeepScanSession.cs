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

    public static async Task<ElevatedDeepScanSession?> StartAsync(string processPath)
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

            await pipe.WaitForConnectionAsync();
            return new ElevatedDeepScanSession(process, pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public async Task<List<FolderStat>> ScanDriveAsync(string driveName)
    {
        var response = await SendAsync(new ElevatedScanRequest
        {
            Command = ElevatedScanCommand.ScanDrive,
            TargetPath = driveName
        });

        return response.TopFolders ?? [];
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

    private async Task<ElevatedScanResponse> SendAsync(ElevatedScanRequest request)
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

    public async ValueTask DisposeAsync()
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

        reader.Dispose();
        await writer.DisposeAsync();
        await pipe.DisposeAsync();
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

    public List<FileSystemEntry>? Children { get; init; }
}

public enum ElevatedScanCommand
{
    ScanDrive,
    LoadChildren,
    Shutdown
}
