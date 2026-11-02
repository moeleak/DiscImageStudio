using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

/// <summary>struct sg_io_hdr from &lt;scsi/sg.h&gt; (88 bytes on 64-bit Linux).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SgIoHeader
{
    public int InterfaceId;
    public int DataDirection;
    public byte CommandLength;
    public byte MaximumSenseLength;
    public ushort IovecCount;
    public uint DataLength;
    public IntPtr Data;
    public IntPtr Command;
    public IntPtr Sense;
    public uint TimeoutMilliseconds;
    public uint Flags;
    public int PackId;
    public IntPtr UserPointer;
    public byte Status;
    public byte MaskedStatus;
    public byte MessageStatus;
    public byte SenseLengthWritten;
    public ushort HostStatus;
    public ushort DriverStatus;
    public int Residual;
    public uint DurationMilliseconds;
    public uint Info;
}

internal static partial class LinuxNative
{
    // asm-generic values shared by x86_64 and aarch64.
    internal const int ReadWrite = 0x2;
    internal const int Exclusive = 0x80;
    internal const int NonBlocking = 0x800;
    internal const int CloseOnExec = 0x80000;
    internal const nuint SgIo = 0x2285;
    internal const int SgDxferNone = -1;
    internal const int SgDxferToDevice = -2;
    internal const int SgDxferFromDevice = -3;

    internal const int ErrorPermission = 1;
    internal const int ErrorNoEntry = 2;
    internal const int ErrorNoDevice = 6;
    internal const int ErrorAccess = 13;
    internal const int ErrorBusy = 16;
    internal const int ErrorNoMedium = 123;

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [SupportedOSPlatform("linux")]
    internal static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [SupportedOSPlatform("linux")]
    internal static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [SupportedOSPlatform("linux")]
    internal static partial int Ioctl(int descriptor, nuint request, ref SgIoHeader header);
}

/// <summary>
/// MMC commands through the SG_IO ioctl on an exclusively opened /dev/srN. SCSI results map to
/// the shared convention: GOOD is success, CHECK CONDITION carries the sense; host or driver
/// failures without sense (timeouts, resets, disconnects) surface as <see cref="IOException"/>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxSgIoTransport : IMmcCommandTransport, IDisposable
{
    private const int SenseCapacity = 64;
    private readonly string _path;
    private readonly MmcWriteTransferLimits _limits;
    private int _descriptor;

    internal LinuxSgIoTransport(string path, int descriptor, int maximumTransferBytes)
    {
        _path = path;
        _descriptor = descriptor;
        _limits = new MmcWriteTransferLimits(0, (uint)maximumTransferBytes);
    }

    public MmcWriteTransferLimits GetWriteTransferLimits() => _limits;

    public MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds)
    {
        ValidateCdb(cdb);
        ArgumentOutOfRangeException.ThrowIfNegative(allocationLength);
        if (allocationLength == 0) return Send(cdb, LinuxNative.SgDxferNone, IntPtr.Zero, 0, timeoutSeconds, out _);
        using AlignedMmcWriteBuffer buffer = new(allocationLength,
            new MmcWriteTransferLimits(0, (uint)Math.Max(allocationLength, _limits.MaximumPageAlignedBytes)));
        MmcCommandResult result = Send(cdb, LinuxNative.SgDxferFromDevice, buffer.Pointer, allocationLength,
            timeoutSeconds, out int residual);
        if (!result.Succeeded) return result;
        int received = Math.Clamp(allocationLength - residual, 0, allocationLength);
        byte[] data = new byte[received];
        Marshal.Copy(buffer.Pointer, data, 0, received);
        return MmcCommandResult.Success(data);
    }

    public MmcCommandResult SendData(byte[] cdb, IntPtr buffer, int bufferLength, uint timeoutSeconds)
    {
        ValidateCdb(cdb);
        if (buffer == IntPtr.Zero || bufferLength <= 0) throw new ArgumentOutOfRangeException(nameof(bufferLength));
        return Send(cdb, LinuxNative.SgDxferToDevice, buffer, bufferLength, timeoutSeconds, out _);
    }

    private unsafe MmcCommandResult Send(byte[] cdb, int direction, IntPtr data, int length, uint timeoutSeconds,
        out int residual)
    {
        ObjectDisposedException.ThrowIf(_descriptor < 0, this);
        byte[] sense = new byte[SenseCapacity];
        SgIoHeader header;
        fixed (byte* command = cdb)
        fixed (byte* senseBuffer = sense)
        {
            header = new SgIoHeader
            {
                InterfaceId = 'S',
                DataDirection = direction,
                CommandLength = (byte)cdb.Length,
                MaximumSenseLength = SenseCapacity,
                DataLength = (uint)length,
                Data = data,
                Command = (IntPtr)command,
                Sense = (IntPtr)senseBuffer,
                TimeoutMilliseconds = (uint)Math.Min(timeoutSeconds * 1000UL, uint.MaxValue - 1),
            };
            if (LinuxNative.Ioctl(_descriptor, LinuxNative.SgIo, ref header) < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new IOException($"SG_IO 调用失败（{_path}，命令 0x{cdb[0]:X2}）：{Marshal.GetPInvokeErrorMessage(error)}"
                    + $"（errno {error}）。", unchecked((int)0x80070000) | error);
            }
        }

        residual = header.Residual;
        int senseLength = Math.Min(header.SenseLengthWritten, (byte)SenseCapacity);
        const byte checkCondition = 0x02;
        const int driverSense = 0x08;
        if (header.Status == checkCondition || ((header.DriverStatus & 0x0F) == driverSense && senseLength > 0))
            return MmcCommandResult.CheckCondition(sense.AsSpan(0, senseLength).ToArray());
        if (header.HostStatus != 0 || (header.DriverStatus & 0x0F) != 0 || header.Status != 0)
            throw new IOException($"光驱命令 0x{cdb[0]:X2} 传输失败（{_path}）：{DescribeHost(header.HostStatus)}，"
                + $"driver_status=0x{header.DriverStatus:X2}，SCSI status=0x{header.Status:X2}。",
                MmcCommandResult.TransportFailureStatus);
        return MmcCommandResult.Success([]);
    }

    private static string DescribeHost(ushort hostStatus) => hostStatus switch
    {
        0 => "host_status=OK",
        1 => "无法连接设备（DID_NO_CONNECT）",
        2 => "总线忙（DID_BUS_BUSY）",
        3 => "命令超时（DID_TIME_OUT）",
        4 => "目标设备无效（DID_BAD_TARGET）",
        5 => "命令被中止（DID_ABORT）",
        7 => "主机适配器错误（DID_ERROR）",
        8 => "总线被重置（DID_RESET）",
        _ => $"host_status=0x{hostStatus:X2}",
    };

    private static void ValidateCdb(byte[] cdb)
    {
        ArgumentNullException.ThrowIfNull(cdb);
        if (cdb.Length is < 6 or > 16) throw new ArgumentOutOfRangeException(nameof(cdb));
    }

    public void Dispose()
    {
        int descriptor = Interlocked.Exchange(ref _descriptor, -1);
        if (descriptor >= 0) _ = LinuxNative.Close(descriptor);
    }
}
