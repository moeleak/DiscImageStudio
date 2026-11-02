using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

[SupportedOSPlatform("windows")]
internal sealed class ImapiMmcCommandTransport(object recorder) : IMmcCommandTransport
{
    private readonly IDiscRecorder2ExCommands _recorder = (IDiscRecorder2ExCommands)recorder;
    private MmcWriteTransferLimits? _transferLimits;

    public MmcWriteTransferLimits GetWriteTransferLimits()
    {
        if (_transferLimits is not null) return _transferLimits;
        Marshal.ThrowExceptionForHR(_recorder.GetByteAlignmentMask(out uint alignment));
        Marshal.ThrowExceptionForHR(_recorder.GetMaximumPageAlignedTransferSize(out uint maximum));
        return _transferLimits = new(alignment, maximum);
    }

    public MmcCommandResult SendData(byte[] cdb, IntPtr buffer, int bufferLength, uint timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(cdb);
        if (cdb.Length is < 6 or > 16 || buffer == IntPtr.Zero || bufferLength <= 0 || timeoutSeconds == 0)
            throw new ArgumentOutOfRangeException(nameof(cdb));
        byte[] sense = new byte[18];
        // The caller owns a stable page/adapter-aligned unmanaged buffer. LPArray
        // marshalling would not guarantee the recorder's data-address alignment.
        int result = _recorder.SendCommandSendDataToDevice(cdb, (uint)cdb.Length, sense,
            timeoutSeconds, buffer, checked((uint)bufferLength));
        return new(result, sense, []);
    }

    public MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(cdb);
        if (cdb.Length is < 6 or > 16 || allocationLength < 0)
            throw new ArgumentOutOfRangeException(nameof(cdb));
        byte[] sense = new byte[18];
        byte[] data = new byte[allocationLength];
        int result;
        if (allocationLength == 0)
            result = _recorder.SendCommandNoData(cdb, (uint)cdb.Length, sense, timeoutSeconds);
        else
        {
            // Read-command payloads have the same adapter alignment contract as
            // writes; ordinary managed arrays do not guarantee that alignment.
            using AlignedMmcWriteBuffer buffer = new(allocationLength, GetWriteTransferLimits());
            result = _recorder.SendCommandGetDataFromDevice(cdb, (uint)cdb.Length, sense,
                timeoutSeconds, buffer.Pointer, (uint)data.Length, out uint fetched);
            if (fetched > data.Length) throw new IOException("IMAPI 返回了超过接收缓冲区的长度。");
            Array.Resize(ref data, (int)fetched);
            if (fetched > 0) Marshal.Copy(buffer.Pointer, data, 0, (int)fetched);
        }
        return new(result, sense, data);
    }
}

// Full IDiscRecorder2Ex vtable, in Windows SDK imapi2.idl order. All entries before
// the transfer-limit queries are required even when this application does not
// invoke them. Buffers returned by the unused allocating methods are IntPtr.
[ComImport, Guid("27354132-7F64-5B0F-8F00-5D77AFBE261E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDiscRecorder2ExCommands
{
    [PreserveSig]
    int SendCommandNoData([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] cdb,
        uint cdbSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 18)] byte[] sense, uint timeout);

    [PreserveSig]
    int SendCommandSendDataToDevice([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] cdb,
        uint cdbSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 18)] byte[] sense, uint timeout,
        IntPtr buffer, uint bufferSize);

    [PreserveSig]
    int SendCommandGetDataFromDevice([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] cdb,
        uint cdbSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 18)] byte[] sense, uint timeout,
        IntPtr buffer, uint bufferSize, out uint fetched);

    [PreserveSig]
    int ReadDvdStructure(uint format, uint address, uint layer, uint agid, out IntPtr data, out uint count);

    [PreserveSig]
    int SendDvdStructure(uint format, IntPtr data, uint count);

    [PreserveSig]
    int GetAdapterDescriptor(out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetDeviceDescriptor(out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetDiscInformation(out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetTrackInformation(uint address, int addressType, out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetFeaturePage(int requestedFeature, [MarshalAs(UnmanagedType.U1)] bool currentFeatureOnly,
        out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetModePage(int requestedModePage, int requestType, out IntPtr data, out uint byteSize);

    [PreserveSig]
    int SetModePage(int requestType, IntPtr data, uint byteSize);

    [PreserveSig]
    int GetSupportedFeaturePages([MarshalAs(UnmanagedType.U1)] bool currentFeatureOnly,
        out IntPtr data, out uint byteSize);

    [PreserveSig]
    int GetSupportedProfiles([MarshalAs(UnmanagedType.U1)] bool currentOnly,
        out IntPtr data, out uint validProfiles);

    [PreserveSig]
    int GetSupportedModePages(int requestType, out IntPtr data, out uint validPages);

    [PreserveSig]
    int GetByteAlignmentMask(out uint value);

    [PreserveSig]
    int GetMaximumNonPageAlignedTransferSize(out uint value);

    [PreserveSig]
    int GetMaximumPageAlignedTransferSize(out uint value);
}
