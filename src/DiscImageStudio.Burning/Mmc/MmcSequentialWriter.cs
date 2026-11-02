using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

/// <summary>What one sequential write covers: optional host-supplied zero sectors before the
/// payload (the CD pregap), the payload itself and zero padding up to a reserved size (DVD-R
/// DAO). LBAs are consecutive from <see cref="StartLba"/>, which may be negative (CD lead-in
/// relative addressing: the pregap starts at -150).</summary>
internal sealed record MmcWritePlan(long StartLba, int SectorBytes, int SectorsPerWrite, int LeadingZeroSectors,
    long PayloadSectors, long TrailingZeroSectors)
{
    internal long TotalSectors => LeadingZeroSectors + PayloadSectors + TrailingZeroSectors;

    internal void Validate()
    {
        if (SectorBytes is not (2048 or 2352)) throw new ArgumentOutOfRangeException(nameof(SectorBytes));
        if (SectorsPerWrite <= 0 || (long)SectorsPerWrite * SectorBytes > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(SectorsPerWrite));
        if (LeadingZeroSectors < 0 || PayloadSectors <= 0 || TrailingZeroSectors < 0)
            throw new ArgumentOutOfRangeException(nameof(PayloadSectors));
    }
}

/// <summary>A single failed WRITE. A timeout leaves the device-side outcome uncertain, so the
/// range is never resent automatically.</summary>
internal sealed class MmcWriteException : IOException
{
    internal MmcWriteException(long lba, int sectors, MmcCommandResult result, Exception? innerException = null)
        : base($"WRITE(10) 0x2A 失败：LBA={lba:N0}，扇区={sectors}；" + new MmcCommandException(0x2A, result).Message
            + (result.Sense.Length == 0 ? string.Empty : $" 原始 Sense={Convert.ToHexString(result.Sense)}。")
            + " 未自动重试此写入命令。", innerException)
    {
        Lba = lba;
        SectorCount = sectors;
        HResult = result.HResult;
    }

    internal long Lba { get; }
    internal int SectorCount { get; }
}

/// <summary>
/// Streams a forward-only source to consecutive LBAs with WRITE(10) and explicit timeouts.
/// Progress counts payload bytes the drive has accepted (possibly into its cache); flushing
/// and closing still decide completion.
/// </summary>
internal static class MmcSequentialWriter
{
    internal const uint WriteTimeoutSeconds = 200;

    internal static void Write(IMmcCommandTransport transport, IStream content, MmcWritePlan plan,
        Action<long, long>? reportPayloadAccepted = null, Func<bool>? isCancellationRequested = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        long payloadBytes = plan.PayloadSectors * plan.SectorBytes;
        content.Stat(out STATSTG stat, 1);
        if (stat.cbSize != payloadBytes)
            throw new ArgumentException("写入数据流长度与计划写入的扇区数不一致。", nameof(content));

        int blockBytes = plan.SectorsPerWrite * plan.SectorBytes;
        using AlignedMmcWriteBuffer buffer = new(blockBytes, transport.GetWriteTransferLimits());
        byte[] block = new byte[blockBytes];
        IntPtr countPointer = Marshal.AllocHGlobal(sizeof(long));
        Stopwatch sinceReport = Stopwatch.StartNew();
        try
        {
            Publish(reportPayloadAccepted, 0, payloadBytes);
            long written = 0;
            while (written < plan.TotalSectors)
            {
                ThrowIfCancelled(isCancellationRequested);
                int sectors = (int)Math.Min(plan.SectorsPerWrite, plan.TotalSectors - written);
                int bytes = sectors * plan.SectorBytes;
                FillBlock(content, plan, written, sectors, block, countPointer, isCancellationRequested);
                Marshal.Copy(block, 0, buffer.Pointer, bytes);
                long lba = plan.StartLba + written;
                byte[] cdb = MmcCommandSet.CreateWrite10(lba, sectors);
                MmcCommandResult result;
                try { result = transport.SendData(cdb, buffer.Pointer, bytes, WriteTimeoutSeconds); }
                catch (IOException exception)
                {
                    throw new MmcWriteException(lba, sectors, new(exception.HResult, [], []), exception);
                }

                if (!result.Succeeded) throw new MmcWriteException(lba, sectors, result);
                written += sectors;
                long accepted = Math.Clamp(written - plan.LeadingZeroSectors, 0, plan.PayloadSectors) * plan.SectorBytes;
                if (sinceReport.ElapsedMilliseconds >= 250 || written == plan.TotalSectors)
                {
                    Publish(reportPayloadAccepted, accepted, payloadBytes);
                    sinceReport.Restart();
                }

                // An accepted command cannot be undone; check only between commands.
                ThrowIfCancelled(isCancellationRequested);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(countPointer);
        }
    }

    private static void FillBlock(IStream content, MmcWritePlan plan, long firstSector, int sectors, byte[] block,
        IntPtr countPointer, Func<bool>? isCancellationRequested)
    {
        Array.Clear(block, 0, sectors * plan.SectorBytes);
        long payloadStart = plan.LeadingZeroSectors;
        long payloadEnd = payloadStart + plan.PayloadSectors;
        long from = Math.Max(firstSector, payloadStart);
        long to = Math.Min(firstSector + sectors, payloadEnd);
        if (from >= to) return;
        int offset = checked((int)((from - firstSector) * plan.SectorBytes));
        int length = checked((int)((to - from) * plan.SectorBytes));
        ReadExactly(content, block, offset, length, countPointer, isCancellationRequested);
    }

    private static void ReadExactly(IStream content, byte[] destination, int offset, int length, IntPtr countPointer,
        Func<bool>? isCancellationRequested)
    {
        byte[] scratch = offset == 0 ? destination : new byte[length];
        int filled = 0;
        while (filled < length)
        {
            ThrowIfCancelled(isCancellationRequested);
            int requested = length - filled;
            byte[] target = filled == 0 ? scratch : new byte[requested];
            Marshal.WriteInt32(countPointer, 0);
            content.Read(target, requested, countPointer);
            int read = Marshal.ReadInt32(countPointer);
            if (read <= 0 || read > requested)
                throw new EndOfStreamException($"刻录数据流未提供完整写入块：已读取 {filled} / {length} 字节。");
            if (!ReferenceEquals(target, destination) || offset != 0)
                Buffer.BlockCopy(target, 0, destination, offset + filled, read);
            filled += read;
        }
    }

    private static void ThrowIfCancelled(Func<bool>? isCancellationRequested)
    {
        if (isCancellationRequested?.Invoke() == true) throw new OperationCanceledException("刻录已取消。");
    }

    private static void Publish(Action<long, long>? report, long completed, long total)
    {
        try { report?.Invoke(completed, total); }
        catch { /* Progress observers must not interrupt a physical write. */ }
    }
}
