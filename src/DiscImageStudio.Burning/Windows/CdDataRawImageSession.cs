using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using System.Runtime.Versioning;

namespace DiscImageStudio.Burning;

/// <summary>
/// Converts a cooked Mode 1 ISO into a DAO-96 image. IMAPI's image creator only
/// accepts audio, so it is used solely for the standard lead-in/TOC/lead-out and
/// subchannel timing. Every program sector is replaced by a fully encoded Mode 1
/// sector, and Q identifies a data track throughout the session.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CdDataRawImageSession : ICdRawImageSession
{
    private readonly DynamicCdRawImageSession _templateSession = new();
    private CdDataRawImageStream? _image;
    private bool _disposed;

    public long StartOfLeadout => _templateSession.StartOfLeadout;

    public IStream CreateImage(IStream isoContent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(isoContent);
        if (_image is not null) throw new InvalidOperationException("The CD DAO image has already been created.");
        isoContent.Stat(out STATSTG stat, 1);
        if (stat.cbSize < 300L * 2048 || stat.cbSize % 2048 != 0)
            throw new ArgumentException("CD DAO data tracks require at least 300 complete 2048-byte sectors.", nameof(isoContent));
        long sectors = stat.cbSize / 2048;
        IStream template = _templateSession.CreateImage(new ZeroAudioStream(checked(sectors * 2352)));
        if (_templateSession.StartOfLeadout != sectors)
            throw new InvalidOperationException("IMAPI changed the requested CD data track length.");
        _image = new CdDataRawImageStream(template, isoContent, sectors);
        return _image;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _image?.Dispose();
        _templateSession.Dispose();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ZeroAudioStream(long length) : IStream
    {
        private long _position;
        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            ArgumentNullException.ThrowIfNull(pv);
            if ((uint)cb > pv.Length) throw new ArgumentOutOfRangeException(nameof(cb));
            int count = (int)Math.Min(cb, length - _position);
            Array.Clear(pv, 0, count);
            _position += count;
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, count);
        }
        public void Seek(long offset, int origin, IntPtr newPosition)
        {
            long target = origin switch { 0 => offset, 1 => checked(_position + offset), 2 => checked(length + offset), _ => -1 };
            if (target < 0 || target > length) throw new COMException("Invalid template position.", unchecked((int)0x80030001));
            _position = target;
            if (newPosition != IntPtr.Zero) Marshal.WriteInt64(newPosition, _position);
        }
        public void Stat(out STATSTG stat, int flags) => stat = new() { type = 2, cbSize = length, grfMode = 0 };
        public void Write(byte[] p, int c, IntPtr n) => throw new NotSupportedException();
        public void SetSize(long size) => throw new NotSupportedException();
        public void CopyTo(IStream stream, long count, IntPtr read, IntPtr written) => throw new NotSupportedException();
        public void Commit(int flags) { }
        public void Revert() => throw new NotSupportedException();
        public void LockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void UnlockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void Clone(out IStream stream) => throw new NotSupportedException();
    }
}
