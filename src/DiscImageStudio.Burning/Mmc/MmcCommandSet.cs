using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiscImageStudio.Burning;

/// <summary>MMC feature profiles reported by GET CONFIGURATION (MMC-6 table 87).</summary>
internal static class MmcProfiles
{
    internal const int CdR = 0x09;
    internal const int CdRw = 0x0A;
    internal const int DvdRSequential = 0x11;
    internal const int DvdRam = 0x12;
    internal const int DvdRwRestricted = 0x13;
    internal const int DvdRwSequential = 0x14;
    internal const int DvdRDualLayerSequential = 0x15;
    internal const int DvdRDualLayerJump = 0x16;
    internal const int DvdPlusRw = 0x1A;
    internal const int DvdPlusR = 0x1B;
    internal const int DvdPlusRwDualLayer = 0x2A;
    internal const int DvdPlusRDualLayer = 0x2B;
    internal const int BluRayRSequential = 0x41;
    internal const int BluRayRRandom = 0x42;
    internal const int BluRayRe = 0x43;

    internal static bool IsDvdMinusR(int profile) => profile is DvdRSequential or DvdRDualLayerSequential;

    internal static bool IsDvdPlusR(int profile) => profile is DvdPlusR or DvdPlusRDualLayer;

    internal static string Describe(int profile) => profile switch
    {
        0 => "无盘片",
        CdR => "CD-R",
        CdRw => "CD-RW",
        0x08 => "CD-ROM",
        0x10 => "DVD-ROM",
        DvdRSequential => "DVD-R",
        DvdRam => "DVD-RAM",
        DvdRwRestricted or DvdRwSequential => "DVD-RW",
        DvdRDualLayerSequential or DvdRDualLayerJump => "DVD-R DL",
        DvdPlusRw => "DVD+RW",
        DvdPlusR => "DVD+R",
        DvdPlusRwDualLayer => "DVD+RW DL",
        DvdPlusRDualLayer => "DVD+R DL",
        0x40 => "BD-ROM",
        BluRayRSequential => "BD-R（顺序记录）",
        BluRayRRandom => "BD-R（随机记录）",
        BluRayRe => "BD-RE",
        _ => $"未知介质（profile 0x{profile:X4}）",
    };
}

/// <summary>READ DISC INFORMATION, standard disc information block (MMC-6 6.22.3.1).</summary>
internal sealed record MmcDiscInformation(int DiscStatus, int LastSessionState, int FirstTrack, int LastTrackInLastSession,
    long LastPossibleLeadOutStartLba)
{
    internal const int Empty = 0;
    internal const int Incomplete = 1;
    internal const int Complete = 2;
    internal bool IsBlank => DiscStatus == Empty;
}

/// <summary>READ TRACK INFORMATION (MMC-6 6.27.3).</summary>
internal sealed record MmcTrackInformation(int TrackNumber, bool Blank, bool NextWritableAddressValid,
    uint TrackStart, uint NextWritableAddress, uint FreeBlocks, uint TrackSize);

/// <summary>One GET PERFORMANCE type 03h write speed descriptor (MMC-6 6.8.2.7).</summary>
internal sealed record MmcWriteSpeedDescriptor(int WriteRotationControl, uint EndLba, uint ReadKilobytesPerSecond,
    uint WriteKilobytesPerSecond);

/// <summary>
/// Platform-neutral MMC command set for the Linux SG_IO and macOS SCSITask backends: device
/// state queries, speed control, write parameters and session closing. Every failing command
/// surfaces as <see cref="MmcCommandException"/> with the device sense; nothing here retries a
/// command whose effect is uncertain.
/// </summary>
internal sealed class MmcCommandSet(IMmcCommandTransport transport)
{
    internal const uint DefaultTimeoutSeconds = 30;
    internal const uint LongTimeoutSeconds = 600;

    internal IMmcCommandTransport Transport => transport;

    internal MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds = DefaultTimeoutSeconds)
    {
        MmcCommandResult result = transport.Execute(cdb, allocationLength, timeoutSeconds);
        if (!result.Succeeded) throw new MmcCommandException(cdb[0], result);
        return result;
    }

    /// <summary>A data-out command with a small parameter list (MODE SELECT, SET STREAMING,
    /// SEND CUE SHEET), copied into an adapter-aligned buffer.</summary>
    internal void SendParameters(byte[] cdb, ReadOnlySpan<byte> parameters, uint timeoutSeconds = DefaultTimeoutSeconds)
    {
        if (parameters.IsEmpty) throw new ArgumentException("A parameter list is required.", nameof(parameters));
        using AlignedMmcWriteBuffer buffer = new(parameters.Length, transport.GetWriteTransferLimits());
        byte[] copy = parameters.ToArray();
        Marshal.Copy(copy, 0, buffer.Pointer, copy.Length);
        MmcCommandResult result = transport.SendData(cdb, buffer.Pointer, copy.Length, timeoutSeconds);
        if (!result.Succeeded) throw new MmcCommandException(cdb[0], result);
    }

    internal (string Vendor, string Product, string Revision) Inquiry()
    {
        byte[] data = Execute([0x12, 0, 0, 0, 96, 0], 96).Data;
        if (data.Length < 36) throw new IOException("INQUIRY 返回的数据被截断。");
        static string Text(byte[] bytes, int offset, int length)
            => System.Text.Encoding.ASCII.GetString(bytes, offset, length).Trim('\0', ' ');
        return (Text(data, 8, 8), Text(data, 16, 16), Text(data, 32, 4));
    }

    /// <summary>Waits through "becoming ready" and "operation in progress"; a missing medium
    /// fails immediately with an actionable message.</summary>
    internal void WaitUntilReady(TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromMinutes(10);
        Stopwatch elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                Execute([0, 0, 0, 0, 0, 0], 0);
                return;
            }
            catch (MmcCommandException exception) when (exception.IsNoMedium)
            {
                throw new InvalidOperationException("光驱中没有盘片，或托盘未关闭。", exception);
            }
            catch (MmcCommandException exception) when (exception.IsNotReadyTransient)
            {
                if (elapsed.Elapsed >= limit) throw new TimeoutException("等待光驱就绪超时。", exception);
                Thread.Sleep(200);
            }
        }
    }

    internal int ReadCurrentProfile()
    {
        byte[] data = Execute(ReadCdb(0x46, 8), 8).Data;
        if (data.Length < 8) throw new IOException("GET CONFIGURATION 返回的数据被截断。");
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6, 2));
    }

    /// <summary>The feature descriptor's payload if the drive reports the feature as current,
    /// otherwise null (RT=10b: exactly the requested feature).</summary>
    internal byte[]? ReadCurrentFeature(ushort feature)
    {
        byte[] cdb = ReadCdb(0x46, 64);
        cdb[1] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(2, 2), feature);
        byte[] data = Execute(cdb, 64).Data;
        if (data.Length < 12) return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8, 2)) != feature) return null;
        if ((data[10] & 1) == 0) return null;
        int length = Math.Min(data[11], data.Length - 12);
        return data.AsSpan(12, length).ToArray();
    }

    internal MmcDiscInformation ReadDiscInformation()
    {
        byte[] data = Execute(ReadCdb(0x51, 34), 34).Data;
        if (data.Length < 24) throw new IOException("READ DISC INFORMATION 返回的数据被截断。");
        int status = data[2] & 3;
        int lastSession = (data[2] >> 2) & 3;
        int firstTrack = data[3];
        int lastTrack = data[6] | data.ElementAtOrDefault(11) << 8;
        // CD: bytes 21..23 hold the last possible lead-out start as MSF.
        long leadOut = MsfToLba(data[21], data[22], data[23]);
        return new MmcDiscInformation(status, lastSession, firstTrack, lastTrack, leadOut);
    }

    internal MmcTrackInformation ReadTrackInformation(int trackNumber)
    {
        byte[] cdb = ReadCdb(0x52, 48);
        cdb[1] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(cdb.AsSpan(2, 4), checked((uint)trackNumber));
        byte[] data = Execute(cdb, 48).Data;
        if (data.Length < 28) throw new IOException("READ TRACK INFORMATION 返回的数据被截断。");
        int returned = data[2] | (data.Length > 32 ? data[32] << 8 : 0);
        return new MmcTrackInformation(returned, (data[6] & 0x40) != 0, (data[7] & 1) != 0,
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(24, 4)));
    }

    /// <summary>ATIP "last possible start time of lead-out" of a blank CD-R/RW as an LBA
    /// (00:02:00 = LBA 0), or null when the medium reports no ATIP.</summary>
    internal long? ReadAtipLastPossibleLeadOut()
    {
        byte[] cdb = [0x43, 0x02, 0x04, 0, 0, 0, 0, 0, 28, 0];
        byte[] data;
        try { data = Execute(cdb, 28).Data; }
        catch (MmcCommandException exception) when (exception.SenseKey == 5) { return null; }
        if (data.Length < 15 || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0, 2)) < 13) return null;
        return MsfToLba(data[12], data[13], data[14]);
    }

    internal IReadOnlyList<MmcWriteSpeedDescriptor> GetWriteSpeeds()
    {
        const int maxDescriptors = 32;
        int allocation = 8 + maxDescriptors * 16;
        byte[] cdb = [0xAC, 0, 0, 0, 0, 0, 0, 0, 0, maxDescriptors, 0x03, 0];
        byte[] data;
        try { data = Execute(cdb, allocation).Data; }
        catch (MmcCommandException exception) when (exception.SenseKey == 5) { return []; }
        return ParseWriteSpeeds(data);
    }

    internal static IReadOnlyList<MmcWriteSpeedDescriptor> ParseWriteSpeeds(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return [];
        long declared = BinaryPrimitives.ReadUInt32BigEndian(data[..4]) + 4L;
        int end = (int)Math.Min(declared, data.Length);
        List<MmcWriteSpeedDescriptor> result = [];
        for (int offset = 8; offset + 16 <= end; offset += 16)
        {
            ReadOnlySpan<byte> descriptor = data.Slice(offset, 16);
            uint write = BinaryPrimitives.ReadUInt32BigEndian(descriptor[12..]);
            if (write == 0) continue;
            result.Add(new MmcWriteSpeedDescriptor((descriptor[0] >> 3) & 3,
                BinaryPrimitives.ReadUInt32BigEndian(descriptor[4..]),
                BinaryPrimitives.ReadUInt32BigEndian(descriptor[8..]), write));
        }

        return result;
    }

    /// <summary>SET STREAMING with one performance descriptor covering the whole medium.</summary>
    internal void SetStreaming(uint writeKilobytesPerSecond, uint endLba, bool pureCav)
    {
        byte[] descriptor = new byte[28];
        descriptor[0] = (byte)(pureCav ? 1 << 3 : 0);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(8, 4), endLba);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(12, 4), writeKilobytesPerSecond);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(16, 4), 1000);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(20, 4), writeKilobytesPerSecond);
        BinaryPrimitives.WriteUInt32BigEndian(descriptor.AsSpan(24, 4), 1000);
        byte[] cdb = [0xB6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 28, 0];
        SendParameters(cdb, descriptor);
    }

    /// <summary>SET CD SPEED: maximum read speed, the given write speed in kB/s.</summary>
    internal void SetCdSpeed(uint writeKilobytesPerSecond)
    {
        ushort write = (ushort)Math.Min(writeKilobytesPerSecond, ushort.MaxValue);
        Execute([0xBB, 0, 0xFF, 0xFF, (byte)(write >> 8), (byte)write, 0, 0, 0, 0, 0, 0], 0);
    }

    /// <summary>MODE SENSE(10) of the write parameters page, returned without the mode header.</summary>
    internal byte[] ReadWriteParametersPage()
    {
        byte[] cdb = ReadCdb(0x5A, 72);
        cdb[2] = 0x05;
        byte[] data = Execute(cdb, 72).Data;
        if (data.Length < 8) throw new IOException("MODE SENSE 返回的数据被截断。");
        int blockDescriptors = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6, 2));
        int offset = 8 + blockDescriptors;
        if (data.Length < offset + 2 || (data[offset] & 0x3F) != 0x05)
            throw new IOException("光驱未返回写入参数页（mode page 05h）。");
        int length = Math.Min(data[offset + 1] + 2, data.Length - offset);
        if (length < 16) throw new IOException("写入参数页长度无效。");
        return data.AsSpan(offset, length).ToArray();
    }

    internal void WriteWriteParametersPage(ReadOnlySpan<byte> page)
    {
        byte[] parameters = new byte[8 + page.Length];
        page.CopyTo(parameters.AsSpan(8));
        parameters[8] &= 0x3F; // PS is reserved in MODE SELECT.
        byte[] cdb = [0x55, 0x10, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(7, 2), checked((ushort)parameters.Length));
        SendParameters(cdb, parameters);
    }

    internal void ReserveTrack(uint sectors)
    {
        byte[] cdb = new byte[10];
        cdb[0] = 0x53;
        BinaryPrimitives.WriteUInt32BigEndian(cdb.AsSpan(5, 4), sectors);
        Execute(cdb, 0, LongTimeoutSeconds);
    }

    internal void SendCueSheet(ReadOnlySpan<byte> cueSheet)
    {
        byte[] cdb = new byte[10];
        cdb[0] = 0x5D;
        cdb[6] = (byte)(cueSheet.Length >> 16);
        cdb[7] = (byte)(cueSheet.Length >> 8);
        cdb[8] = (byte)cueSheet.Length;
        SendParameters(cdb, cueSheet, LongTimeoutSeconds);
    }

    internal void PreventRemoval(bool prevent) => Execute([0x1E, 0, 0, 0, prevent ? (byte)1 : (byte)0, 0], 0);

    /// <summary>SYNCHRONIZE CACHE with IMMED, then wait until the drive has drained its cache
    /// (and, for DAO/SAO, written the lead-out).</summary>
    internal void SynchronizeCache()
    {
        Execute([0x35, 0x02, 0, 0, 0, 0, 0, 0, 0, 0], 0, 120);
        WaitUntilReady();
    }

    /// <summary>CLOSE TRACK/SESSION with the given close function (MMC-6 6.3).</summary>
    internal void Close(int function, int trackNumber = 0)
    {
        byte[] cdb = [0x5B, 0x01, (byte)(function & 7), 0, (byte)(trackNumber >> 8), (byte)trackNumber, 0, 0, 0, 0];
        Execute(cdb, 0, LongTimeoutSeconds);
        WaitUntilReady();
    }

    internal static byte[] CreateWrite10(long lba, int sectors)
    {
        if (sectors is <= 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(sectors));
        byte[] cdb = new byte[10];
        cdb[0] = 0x2A;
        BinaryPrimitives.WriteInt32BigEndian(cdb.AsSpan(2, 4), checked((int)lba));
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(7, 2), (ushort)sectors);
        return cdb;
    }

    /// <summary>MSF to LBA with lead-in addresses (minute 90+) mapped below zero.</summary>
    internal static long MsfToLba(int minute, int second, int frame)
    {
        long sectors = (minute * 60L + second) * 75 + frame;
        return minute >= 90 ? sectors - 450_150 : sectors - 150;
    }

    internal static (byte Minute, byte Second, byte Frame) LbaToMsf(long lba)
    {
        long sectors = lba + 150;
        if (sectors < 0) sectors += 450_000;
        return ((byte)(sectors / 4500), (byte)(sectors / 75 % 60), (byte)(sectors % 75));
    }

    private static byte[] ReadCdb(byte opcode, ushort allocationLength)
    {
        byte[] cdb = new byte[10];
        cdb[0] = opcode;
        BinaryPrimitives.WriteUInt16BigEndian(cdb.AsSpan(7, 2), allocationLength);
        return cdb;
    }
}
