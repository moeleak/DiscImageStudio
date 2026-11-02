using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// <summary>Sparse test files on every platform. NTFS needs FSCTL_SET_SPARSE before the
/// length is extended; Unix file systems leave a hole when a file is extended with SetLength.</summary>
internal static class SparseFiles
{
    internal static void MarkSparse(FileStream stream, string failureMessage)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), failureMessage);
    }

    /// <summary>True when the platform exposes a sparse attribute (Windows) and it is set; always true elsewhere.</summary>
    internal static bool HasSparseAttribute(string path)
        => !OperatingSystem.IsWindows() || (File.GetAttributes(path) & FileAttributes.SparseFile) != 0;

    internal static long GetAllocatedBytes(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Marshal.SetLastPInvokeError(0);
            uint low = GetCompressedFileSize(path, out uint high);
            int error = Marshal.GetLastWin32Error();
            if (low == uint.MaxValue && error != 0) throw new Win32Exception(error);
            return checked((long)(((ulong)high << 32) | low));
        }

        // du -k reports allocated (not logical) kibibytes on both Linux and macOS.
        ProcessStartInfo start = new("du", ["-k", path]) { RedirectStandardOutput = true, UseShellExecute = false };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("du could not be started.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"du failed for {path}.");
        string kibibytes = output.Split(['\t', ' '], 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return checked(long.Parse(kibibytes, CultureInfo.InvariantCulture) * 1024);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
        IntPtr inputBuffer, uint inputBufferSize, IntPtr outputBuffer, uint outputBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSize(string filename, out uint high);
}
