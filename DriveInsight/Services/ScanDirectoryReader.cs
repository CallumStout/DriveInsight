using System;
using System.ComponentModel;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DriveInsight.Services;

// Only materialize names for directories and files the caller actually needs.
internal delegate void ScanEntryVisitor(ReadOnlySpan<char> name, long bytes, bool isDirectory);

internal sealed class ScanDirectoryReader : IDisposable
{
    private const int BufferSize = 64 * 1024;
    private const int HeaderSize = 88; // FILE_ID_EXTD_DIR_INFO.FileName
    private const uint NameSurrogate = 0x20000000;

    private IntPtr _buffer;

    public void Dispose()
    {
        if (_buffer == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }

    public static int Read(string path, ScanEntryVisitor visitor, CancellationToken ct)
    {
        using var reader = new ScanDirectoryReader();
        return reader.ReadEntries(path, visitor, ct);
    }

    public int ReadEntries(string path, ScanEntryVisitor visitor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows() && TryReadWindows(path, visitor, ct, out var skipped))
            return skipped;
        return ReadManaged(path, visitor, ct);
    }

    internal unsafe bool TryReadWindows(
        string path, ScanEntryVisitor visitor, CancellationToken ct, out int skipped)
    {
        skipped = 0;
        // Backup semantics also permits opening directory handles. Only the elevated
        // helper enables SeBackupPrivilege; the normal process keeps its usual access.
        using var handle = CreateFileW(ToExtendedPath(path), 1, 7, IntPtr.Zero, 3,
            0x02000000 | 0x00200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Validate the opened handle as well as enumerated children: a queued directory
        // may have been replaced by a junction while the scan was running.
        var tagInfo = stackalloc uint[2];
        if (!GetFileInformationByHandleEx(handle, 9, (IntPtr)tagInfo, 8))
            return false;
        if (ShouldSkip((FileAttributes)tagInfo[0], tagInfo[1]))
        {
            skipped = 1;
            return true;
        }

        if (_buffer == IntPtr.Zero) _buffer = Marshal.AllocHGlobal(BufferSize);
        var buffer = _buffer;
        var first = true;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!GetFileInformationByHandleEx(handle, first ? 20 : 19, buffer, BufferSize))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 18) return true; // ERROR_NO_MORE_FILES
                // Some network/file-system drivers do not support extended entries.
                // Fall back only before emitting records to avoid double counting.
                if (first && error is 1 or 50 or 87 or 124) return false;
                throw new Win32Exception(error);
            }

            first = false;
            var offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (offset < 0 || offset > BufferSize - HeaderSize)
                    throw new IOException("Invalid directory record offset.");
                var record = (byte*)buffer + offset;
                var next = *(uint*)record;
                var nameBytes = *(uint*)(record + 60);
                if ((nameBytes & 1) != 0 || nameBytes > BufferSize - offset - HeaderSize ||
                    (next != 0 && (next < HeaderSize + nameBytes || next > BufferSize - offset)))
                    throw new IOException("Invalid directory record length.");
                var name = new ReadOnlySpan<char>(record + HeaderSize, (int)nameBytes / 2);
                if (!name.SequenceEqual(".") && !name.SequenceEqual(".."))
                {
                    var attributes = (FileAttributes)(*(uint*)(record + 56));
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (isDirectory && ShouldSkip(attributes, *(uint*)(record + 68)))
                        skipped++;
                    else
                        visitor(name, Math.Max(0, *(long*)(record + 40)), isDirectory);
                }
                if (next == 0) break;
                offset += (int)next;
            }
        }
    }

    internal static bool ShouldSkip(FileAttributes attributes, uint tag) =>
        (attributes & FileAttributes.ReparsePoint) != 0 &&
        (tag == 0 || (tag & NameSurrogate) != 0);

    internal static int ReadManaged(string path, ScanEntryVisitor visitor, CancellationToken ct)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return 1;
        var skipped = 0;
        var entries = new FileSystemEnumerable<byte>(path, (ref FileSystemEntry entry) =>
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                skipped++;
            else
                visitor(entry.FileName, Math.Max(0, entry.Length), entry.IsDirectory);
            return 0;
        }, new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            BufferSize = BufferSize
        });
        foreach (var unused in entries) ct.ThrowIfCancellationRequested();
        return skipped;
    }

    private static string ToExtendedPath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal)
        ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
        IntPtr buffer, uint size);
}
