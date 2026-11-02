using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

/// <summary>CFUUIDBytes: a UUID passed by value to CoreFoundation and IUnknown QueryInterface.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CfUuidBytes
{
    public byte B0, B1, B2, B3, B4, B5, B6, B7, B8, B9, B10, B11, B12, B13, B14, B15;

    internal static CfUuidBytes From(Guid value)
    {
        // Guid's string form is the big-endian RFC 4122 byte order CoreFoundation uses.
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes, bigEndian: true, out _)) throw new InvalidOperationException();
        return MemoryMarshal.Read<CfUuidBytes>(bytes);
    }
}

/// <summary>IOVirtualRange for 64-bit processes (IOVirtualAddress and IOByteCount are 64-bit).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IoVirtualRange
{
    public ulong Address;
    public ulong Length;
}

/// <summary>IOKit / CoreFoundation entry points and the SCSITaskLib plug-in identifiers.</summary>
internal static partial class MacNative
{
    private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // SCSITaskLib.h / IOCFPlugIn.h.
    internal static readonly Guid MmcDeviceUserClientTypeId = new("97ABCF2C-23CC-11D5-A0E8-003065704866");
    internal static readonly Guid MmcDeviceInterfaceId = new("1F651106-23CC-11D5-BBDB-003065704866");
    internal static readonly Guid CfPlugInInterfaceId = new("C244E858-109C-11D4-91D4-0050E4C6426F");

    internal const int Success = 0;
    internal const int ReturnNotPrivileged = unchecked((int)0xE00002C1);
    internal const int ReturnExclusiveAccess = unchecked((int)0xE00002C5);
    internal const int ReturnBusy = unchecked((int)0xE00002D5);
    internal const uint StringEncodingUtf8 = 0x08000100;
    internal const uint IterateRecursively = 1;
    internal const uint IterateParents = 2;

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr IOServiceMatching(string name);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial IntPtr IORegistryEntryIDMatching(ulong entryId);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial uint IOIteratorNext(uint iterator);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial int IOObjectRelease(uint objectHandle);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong entryId);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr IORegistryEntrySearchCFProperty(uint entry, string plane, IntPtr key,
        IntPtr allocator, uint options);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial int IOCreatePlugInInterfaceForService(uint service, IntPtr pluginType,
        IntPtr interfaceType, out IntPtr plugIn, out int score);

    [SupportedOSPlatform("macos")]
    [LibraryImport(IoKit)]
    internal static partial int IODestroyPlugInInterface(IntPtr plugIn);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFStringGetCString(IntPtr text, byte[] buffer, nint bufferSize, uint encoding);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial nuint CFGetTypeID(IntPtr value);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial nuint CFStringGetTypeID();

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial nuint CFDictionaryGetTypeID();

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFUUIDCreateFromUUIDBytes(IntPtr allocator, CfUuidBytes bytes);

    [SupportedOSPlatform("macos")]
    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(IntPtr value);

    internal static Exception DescribeExclusiveFailure(string name, int result) => result switch
    {
        ReturnBusy or ReturnExclusiveAccess => new InvalidOperationException(
            $"{name} 正被系统或其他程序占用：盘片可能已挂载（请在访达中推出卷，或运行 diskutil unmount），"
            + "或其他刻录软件正在使用光驱。"),
        ReturnNotPrivileged => new UnauthorizedAccessException($"没有独占访问 {name} 的权限。"),
        _ => new IOException($"无法独占访问 {name}（IOKit 0x{result:X8}）。"),
    };

    [SupportedOSPlatform("macos")]
    internal static IntPtr CreateString(string text)
        => CFStringCreateWithCString(IntPtr.Zero, text, StringEncodingUtf8);

    [SupportedOSPlatform("macos")]
    internal static string? ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID()) return null;
        byte[] buffer = new byte[512];
        return CFStringGetCString(value, buffer, buffer.Length, StringEncodingUtf8)
            ? System.Text.Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is int end and >= 0 ? end : buffer.Length)
            : null;
    }
}

/// <summary>
/// MMC commands through the IOKit SCSITask user client of an authoring device (SCSITaskLib):
/// the MMC device interface yields a SCSITaskDevice with exclusive access, and each command is
/// one synchronous SCSITask. Vtable offsets follow SCSITaskLib.h for 64-bit processes
/// (IUnknown: 0 reserved, 8 QueryInterface, 16 AddRef, 24 Release; then version/revision).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacScsiTaskTransport : IMmcCommandTransport, IDisposable
{
    private const int MaximumTransferBytes = 128 * 1024;
    private const byte NoDataTransfer = 0;
    private const byte ToTarget = 1;
    private const byte FromTarget = 2;
    private const uint StatusCheckCondition = 0x02;

    // SCSITaskDeviceInterface.
    private const int ObtainExclusiveAccessSlot = 64;
    private const int ReleaseExclusiveAccessSlot = 72;
    private const int CreateScsiTaskSlot = 80;
    // SCSITaskInterface.
    private const int SetCommandDescriptorBlockSlot = 64;
    private const int SetScatterGatherEntriesSlot = 88;
    private const int SetTimeoutDurationSlot = 96;
    private const int ExecuteTaskSyncSlot = 128;
    // MMCDeviceInterface.
    private const int GetScsiTaskDeviceInterfaceSlot = 136;
    // IUnknown.
    private const int QueryInterfaceSlot = 8;
    private const int ReleaseSlot = 24;

    private readonly string _name;
    private uint _service;
    private IntPtr _plugIn;
    private IntPtr _mmc;
    private IntPtr _taskDevice;
    private bool _exclusive;

    private MacScsiTaskTransport(string name, uint service)
    {
        _name = name;
        _service = service;
    }

    /// <summary>Takes ownership of <paramref name="service"/>.</summary>
    internal static MacScsiTaskTransport Open(string name, uint service)
    {
        MacScsiTaskTransport transport = new(name, service);
        try
        {
            transport.Connect();
            return transport;
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private void Connect()
    {
        IntPtr pluginType = MacNative.CFUUIDCreateFromUUIDBytes(IntPtr.Zero, CfUuidBytes.From(MacNative.MmcDeviceUserClientTypeId));
        IntPtr interfaceType = MacNative.CFUUIDCreateFromUUIDBytes(IntPtr.Zero, CfUuidBytes.From(MacNative.CfPlugInInterfaceId));
        try
        {
            int created = MacNative.IOCreatePlugInInterfaceForService(_service, pluginType, interfaceType, out _plugIn, out _);
            if (created != MacNative.Success || _plugIn == IntPtr.Zero)
                throw new IOException($"无法连接 {_name} 的 MMC 刻录接口（IOKit 0x{created:X8}）。");
        }
        finally
        {
            MacNative.CFRelease(pluginType);
            MacNative.CFRelease(interfaceType);
        }

        IntPtr mmc;
        var queryInterface = (delegate* unmanaged<IntPtr, CfUuidBytes, IntPtr*, int>)Slot(_plugIn, QueryInterfaceSlot);
        int queried = queryInterface(_plugIn, CfUuidBytes.From(MacNative.MmcDeviceInterfaceId), &mmc);
        if (queried != 0 || mmc == IntPtr.Zero)
            throw new IOException($"{_name} 不提供 MMC 设备接口（0x{queried:X8}）。");
        _mmc = mmc;

        var getTaskDevice = (delegate* unmanaged<IntPtr, IntPtr>)Slot(_mmc, GetScsiTaskDeviceInterfaceSlot);
        _taskDevice = getTaskDevice(_mmc);
        if (_taskDevice == IntPtr.Zero) throw new IOException($"{_name} 未返回 SCSITask 设备接口。");

        var obtain = (delegate* unmanaged<IntPtr, int>)Slot(_taskDevice, ObtainExclusiveAccessSlot);
        int exclusive = obtain(_taskDevice);
        if (exclusive != MacNative.Success) throw MacNative.DescribeExclusiveFailure(_name, exclusive);
        _exclusive = true;
    }

    public MmcWriteTransferLimits GetWriteTransferLimits() => new(0, MaximumTransferBytes);

    public MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds)
    {
        ValidateCdb(cdb);
        ArgumentOutOfRangeException.ThrowIfNegative(allocationLength);
        if (allocationLength == 0) return Run(cdb, NoDataTransfer, IntPtr.Zero, 0, timeoutSeconds, out _);
        using AlignedMmcWriteBuffer buffer = new(allocationLength,
            new MmcWriteTransferLimits(0, (uint)Math.Max(allocationLength, MaximumTransferBytes)));
        MmcCommandResult result = Run(cdb, FromTarget, buffer.Pointer, allocationLength, timeoutSeconds, out ulong transferred);
        if (!result.Succeeded) return result;
        int received = (int)Math.Min(transferred, (ulong)allocationLength);
        byte[] data = new byte[received];
        Marshal.Copy(buffer.Pointer, data, 0, received);
        return MmcCommandResult.Success(data);
    }

    public MmcCommandResult SendData(byte[] cdb, IntPtr buffer, int bufferLength, uint timeoutSeconds)
    {
        ValidateCdb(cdb);
        if (buffer == IntPtr.Zero || bufferLength <= 0) throw new ArgumentOutOfRangeException(nameof(bufferLength));
        return Run(cdb, ToTarget, buffer, bufferLength, timeoutSeconds, out _);
    }

    private MmcCommandResult Run(byte[] cdb, byte direction, IntPtr data, int length, uint timeoutSeconds,
        out ulong transferred)
    {
        ObjectDisposedException.ThrowIf(_taskDevice == IntPtr.Zero, this);
        var create = (delegate* unmanaged<IntPtr, IntPtr>)Slot(_taskDevice, CreateScsiTaskSlot);
        IntPtr task = create(_taskDevice);
        if (task == IntPtr.Zero) throw new IOException($"{_name}：无法创建 SCSI 任务。");
        try
        {
            fixed (byte* command = cdb)
            {
                var setCdb = (delegate* unmanaged<IntPtr, byte*, byte, int>)Slot(task, SetCommandDescriptorBlockSlot);
                Check(setCdb(task, command, (byte)cdb.Length), cdb[0], "设置命令块");
            }

            if (length > 0)
            {
                IoVirtualRange range = new() { Address = (ulong)data, Length = (ulong)length };
                var setRanges = (delegate* unmanaged<IntPtr, IoVirtualRange*, byte, ulong, byte, int>)Slot(task, SetScatterGatherEntriesSlot);
                Check(setRanges(task, &range, 1, (ulong)length, direction), cdb[0], "设置数据缓冲区");
            }

            var setTimeout = (delegate* unmanaged<IntPtr, uint, int>)Slot(task, SetTimeoutDurationSlot);
            Check(setTimeout(task, (uint)Math.Min(timeoutSeconds * 1000UL, uint.MaxValue)), cdb[0], "设置超时");

            byte* sense = stackalloc byte[32];
            new Span<byte>(sense, 32).Clear();
            uint status = 0;
            ulong count = 0;
            var execute = (delegate* unmanaged<IntPtr, byte*, uint*, ulong*, int>)Slot(task, ExecuteTaskSyncSlot);
            int result = execute(task, sense, &status, &count);
            transferred = count;
            if (result != MacNative.Success)
                throw new IOException($"光驱命令 0x{cdb[0]:X2} 执行失败（{_name}，IOKit 0x{result:X8}）。",
                    MmcCommandResult.TransportFailureStatus);
            if (status == StatusCheckCondition)
                return MmcCommandResult.CheckCondition(new Span<byte>(sense, 18).ToArray());
            if (status != 0)
                throw new IOException($"光驱命令 0x{cdb[0]:X2} 传输失败（{_name}）：{DescribeStatus(status)}。",
                    MmcCommandResult.TransportFailureStatus);
            return MmcCommandResult.Success([]);
        }
        finally
        {
            var release = (delegate* unmanaged<IntPtr, uint>)Slot(task, ReleaseSlot);
            release(task);
        }
    }

    private static string DescribeStatus(uint status) => status switch
    {
        0x01000000 => "任务超时",
        0x02000000 => "协议超时",
        0x03000000 => "设备无响应",
        0x04000000 => "设备不存在",
        0x05000000 => "传输失败",
        _ => $"SCSI 状态 0x{status:X8}",
    };

    private void Check(int result, byte opcode, string step)
    {
        if (result != MacNative.Success)
            throw new IOException($"光驱命令 0x{opcode:X2}：{step}失败（{_name}，IOKit 0x{result:X8}）。");
    }

    /// <summary>The function pointer at <paramref name="offset"/> of an interface's vtable
    /// (an interface reference is a pointer to the vtable pointer).</summary>
    private static IntPtr Slot(IntPtr interfaceReference, int offset)
        => *(IntPtr*)(*(IntPtr*)interfaceReference + offset);

    private static void ValidateCdb(byte[] cdb)
    {
        ArgumentNullException.ThrowIfNull(cdb);
        if (cdb.Length is < 6 or > 16) throw new ArgumentOutOfRangeException(nameof(cdb));
    }

    public void Dispose()
    {
        if (_taskDevice != IntPtr.Zero)
        {
            if (_exclusive)
            {
                var release = (delegate* unmanaged<IntPtr, int>)Slot(_taskDevice, ReleaseExclusiveAccessSlot);
                _ = release(_taskDevice);
                _exclusive = false;
            }

            var releaseInterface = (delegate* unmanaged<IntPtr, uint>)Slot(_taskDevice, ReleaseSlot);
            _ = releaseInterface(_taskDevice);
            _taskDevice = IntPtr.Zero;
        }

        if (_mmc != IntPtr.Zero)
        {
            var release = (delegate* unmanaged<IntPtr, uint>)Slot(_mmc, ReleaseSlot);
            _ = release(_mmc);
            _mmc = IntPtr.Zero;
        }

        if (_plugIn != IntPtr.Zero)
        {
            _ = MacNative.IODestroyPlugInInterface(_plugIn);
            _plugIn = IntPtr.Zero;
        }

        if (_service != 0)
        {
            _ = MacNative.IOObjectRelease(_service);
            _service = 0;
        }
    }
}
