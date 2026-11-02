using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DiscImageStudio.Burning;

/// <summary>
/// The Linux / macOS MMC backend without a drive: every burn sequence runs against a simulated
/// MMC recorder that records each CDB and data-out payload, so the exact command order, write
/// parameters, cue sheet, reservation and finalization are pinned down. Also covers the SG_IO
/// header layout, libc resolution and the sysfs / mountinfo device discovery.
/// </summary>
internal static class MmcBackendTests
{
    internal static void Run()
    {
        TestSgIoLayout();
        TestSpeedConversion();
        TestCueSheet();
        TestMediaPolicy();
        TestBusyRetry();
        TestCdAudioSao();
        TestCdDataSao();
        TestCdOverburnLimit();
        TestDvdMinusRDiscAtOnce();
        TestDvdPlusR();
        TestDvdCapacity();
        TestBluRaySessionSpeed();
        TestBurnerEndToEnd();
        TestLinuxDiscovery();
        TestMacLayout();
        Console.WriteLine("mmc-backend: passed (SG_IO layout, speeds, CD SAO cue sheet, DVD-R DAO, DVD+R finalize, BD session, busy retry, discovery)");
    }

    private static void TestSgIoLayout()
    {
        if (IntPtr.Size != 8) return;
        Require(Marshal.SizeOf<SgIoHeader>() == 88, "sg_io_hdr is 88 bytes on 64-bit Linux");
        (string Field, int Offset)[] offsets =
        [
            (nameof(SgIoHeader.DataDirection), 4), (nameof(SgIoHeader.CommandLength), 8),
            (nameof(SgIoHeader.MaximumSenseLength), 9), (nameof(SgIoHeader.DataLength), 12),
            (nameof(SgIoHeader.Data), 16), (nameof(SgIoHeader.Command), 24), (nameof(SgIoHeader.Sense), 32),
            (nameof(SgIoHeader.TimeoutMilliseconds), 40), (nameof(SgIoHeader.UserPointer), 56),
            (nameof(SgIoHeader.Status), 64), (nameof(SgIoHeader.SenseLengthWritten), 67),
            (nameof(SgIoHeader.HostStatus), 68), (nameof(SgIoHeader.DriverStatus), 70),
            (nameof(SgIoHeader.Residual), 72), (nameof(SgIoHeader.Info), 80),
        ];
        foreach ((string field, int offset) in offsets)
            Require(Marshal.OffsetOf<SgIoHeader>(field).ToInt32() == offset, $"sg_io_hdr.{field} at offset {offset}");

        if (OperatingSystem.IsLinux())
        {
            // Proves the "libc" import resolves and errno flows back.
            int descriptor = LinuxNative.Open("/nonexistent-disc-image-studio-device", LinuxNative.ReadWrite);
            int error = Marshal.GetLastPInvokeError();
            Require(descriptor == -1 && error == LinuxNative.ErrorNoEntry, "libc open reports ENOENT for a missing device");
            Require(LinuxOpticalDeviceProvider.DescribeOpenFailure("/dev/sr0", LinuxNative.ErrorAccess).Message.Contains("cdrom"),
                "EACCES names the cdrom group");
        }
    }

    private static void TestSpeedConversion()
    {
        byte[] response = new byte[8 + 3 * 16];
        BinaryPrimitives.WriteUInt32BigEndian(response, (uint)(response.Length - 4));
        WriteDescriptor(response, 8, wrc: 0, write: 7056);   // CD 40×
        WriteDescriptor(response, 24, wrc: 1, write: 3528);  // CD 20× CAV
        WriteDescriptor(response, 40, wrc: 0, write: 0);     // ignored
        IReadOnlyList<MmcWriteSpeedDescriptor> descriptors = MmcCommandSet.ParseWriteSpeeds(response);
        Require(descriptors.Count == 2, "zero-speed descriptors are dropped");
        IReadOnlyList<OpticalWriteSpeed> cd = MmcSpeedControl.ToSpeeds(descriptors, OpticalBurnMediaKind.CdAudio);
        Require(cd[0] == new OpticalWriteSpeed(1500, true) && cd[1] == new OpticalWriteSpeed(3000, false),
            "CD kB/s convert to 2352-byte sectors/s (75/s at 1×), sorted, WRC=1 is CAV");
        Require(Math.Abs(cd[1].GetMultiplier(OpticalBurnMediaKind.CdAudio) - 40) < 1e-9, "7056 kB/s is 40× CD");
        Require(MmcSpeedControl.ToKilobytesPerSecond(cd[1], OpticalBurnMediaKind.CdAudio) == 7056, "CD speed round-trips to kB/s");
        IReadOnlyList<OpticalWriteSpeed> dvd = MmcSpeedControl.ToSpeeds(
            [new MmcWriteSpeedDescriptor(0, 0, 0, 5540)], OpticalBurnMediaKind.DvdData);
        Require(dvd[0].SectorsPerSecond == 2705 && Math.Round(dvd[0].GetMultiplier(OpticalBurnMediaKind.DvdData)) == 4,
            "DVD kB/s convert to 2048-byte sectors/s");
        Require(MmcCommandSet.MsfToLba(79, 59, 74) == 359_849 && MmcCommandSet.MsfToLba(97, 26, 66) < 0,
            "ATIP MSF maps to program LBAs and lead-in minutes to negative LBAs");
        Require(MmcCommandSet.LbaToMsf(-150) == (0, 0, 0) && MmcCommandSet.LbaToMsf(0) == (0, 2, 0),
            "LBA -150 is 00:00:00 and LBA 0 is 00:02:00");

        static void WriteDescriptor(byte[] buffer, int offset, int wrc, uint write)
        {
            buffer[offset] = (byte)(wrc << 3);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 12), write);
        }
    }

    private static void TestCueSheet()
    {
        byte[] audio = MmcCdSaoBurn.BuildCueSheet(359_849, data: false);
        byte[] expectedAudio =
        [
            0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0,
            0x01, 0x01, 0x00, 0x00, 0, 0, 0, 0,
            0x01, 0x01, 0x01, 0x00, 0, 0, 2, 0,
            0x01, 0xAA, 0x01, 0x01, 0, 79, 59, 74,
        ];
        Require(audio.SequenceEqual(expectedAudio), "audio cue sheet: lead-in, pregap and track from host, lead-out at 79:59:74");
        byte[] data = MmcCdSaoBurn.BuildCueSheet(300, data: true);
        Require(data[0] == 0x41 && data[3] == 0x14 && data[11] == 0x10 && data[19] == 0x10 && data[27] == 0x14
                && data[29] == 0 && data[30] == 6 && data[31] == 0,
            "data cue sheet marks a data track, Mode 1 2048 from the host and the lead-out at 00:06:00");
    }

    private static void TestMediaPolicy()
    {
        MmcMediaPolicy.Require(OpticalBurnMediaKind.CdData, MmcProfiles.CdRw);
        MmcMediaPolicy.Require(OpticalBurnMediaKind.DvdData, MmcProfiles.DvdPlusRDualLayer);
        MmcMediaPolicy.Require(OpticalBurnMediaKind.BluRayData, MmcProfiles.BluRayRSequential);
        Require(Throws<InvalidOperationException>(() => MmcMediaPolicy.Require(OpticalBurnMediaKind.DvdData, MmcProfiles.DvdPlusRw))
            .Contains("暂不支持"), "rewritable DVDs are refused with an explanation");
        Require(Throws<InvalidOperationException>(() => MmcMediaPolicy.Require(OpticalBurnMediaKind.BluRayData, MmcProfiles.BluRayRe))
            .Contains("BD-RE"), "BD-RE is named in the refusal");
        Require(Throws<InvalidOperationException>(() => MmcMediaPolicy.Require(OpticalBurnMediaKind.CdAudio, MmcProfiles.DvdPlusR))
            .Contains("CD-R"), "a DVD in CD mode asks for a CD-R");
    }

    private static void TestBusyRetry()
    {
        SimulatedDrive drive = new() { BusyWrites = 2 };
        MmcBusyRetryTransport transport = new(drive, sleep: _ => { });
        IntPtr buffer = Marshal.AllocHGlobal(2048);
        try
        {
            MmcCommandResult result = transport.SendData(MmcCommandSet.CreateWrite10(0, 1), buffer, 2048, 200);
            Require(result.Succeeded && drive.Log.Count(entry => entry.Cdb[0] == 0x2A) == 3,
                "LONG WRITE IN PROGRESS is resent until the drive accepts the WRITE");
            drive.Log.Clear();
            drive.IllegalWrites = 1;
            result = transport.SendData(MmcCommandSet.CreateWrite10(16, 1), buffer, 2048, 200);
            Require(!result.Succeeded && drive.Log.Count == 1, "an ILLEGAL REQUEST is never resent");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void TestCdAudioSao()
    {
        const int sectors = 300;
        SimulatedDrive drive = new() { Profile = MmcProfiles.CdR };
        byte[] payload = Pattern(sectors * 2352, 7);
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.CdAudio, payload.Length, (_, _) => { },
            new OpticalWriteSpeed(1200, false));
        long accepted = 0;
        OpticalWriteSpeed speed = MmcCdSaoBurn.Burn(new MmcCommandSet(drive), new ByteStream(payload), request,
            CancellationToken.None, reportPayloadAccepted: (done, _) => accepted = done);
        Require(speed.SectorsPerSecond == 1200, "the requested CD speed is reported");
        Require(drive.Opcodes().SequenceEqual(new byte[]
            { 0x1E, 0x51, 0x43, 0x5A, 0x55, 0xBB, 0x5D }.Concat(Enumerable.Repeat((byte)0x2A, 17)).Concat(new byte[] { 0x35, 0x00, 0x1E })),
            "CD SAO: lock, blank check, ATIP, page 05h, speed, cue sheet, writes, flush, unlock");
        byte[] page = drive.DataOut(0x55)[8..];
        Require(page[2] == 0x42 && page[3] == 0 && page[4] == 0 && page[15] == 150,
            "page 05h selects SAO with BUFE, audio track mode, raw 2352 and a 150-frame pause");
        Require(drive.Cdb(0xBB).AsSpan(4, 2).SequenceEqual(new byte[] { 0x0B, 0x06 }), "1200 sectors/s is 2822 kB/s");
        Require(drive.DataOut(0x5D).SequenceEqual(MmcCdSaoBurn.BuildCueSheet(sectors, false)), "the cue sheet is sent as built");
        byte[] firstWrite = drive.Log.First(entry => entry.Cdb[0] == 0x2A).Cdb;
        Require(BinaryPrimitives.ReadInt32BigEndian(firstWrite.AsSpan(2)) == -150 && firstWrite[8] == 27,
            "the first WRITE starts at the pregap (LBA -150) with 27 audio sectors");
        Require(drive.WrittenFrom(-150).AsSpan(0, 150 * 2352).IndexOfAnyExcept((byte)0) < 0
                && drive.WrittenFrom(-150).AsSpan(150 * 2352).SequenceEqual(payload),
            "the pregap is silence and the payload follows byte for byte");
        Require(accepted == payload.Length, "progress counts payload bytes only");
    }

    private static void TestCdDataSao()
    {
        const int sectors = 300;
        SimulatedDrive drive = new() { Profile = MmcProfiles.CdR };
        byte[] payload = Pattern(sectors * 2048, 11);
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.CdData, payload.Length, (_, _) => { });
        OpticalWriteSpeed speed = MmcCdSaoBurn.Burn(new MmcCommandSet(drive), new ByteStream(payload), request,
            CancellationToken.None);
        Require(speed.SectorsPerSecond == OpticalBurnWorkflows.CdDaoFallbackSpeedSectorsPerSecond,
            "without a reported speed CD uses the DAO fallback speed");
        byte[] page = drive.DataOut(0x55)[8..];
        Require(page[3] == 0x04 && page[4] == 0x08, "data uses track mode 4 and Mode 1 blocks");
        Require(drive.Log.Where(entry => entry.Cdb[0] == 0x2A).All(entry => entry.Data.Length % 2048 == 0)
                && drive.WrittenFrom(-150).Length == (sectors + 150) * 2048
                && drive.WrittenFrom(-150).AsSpan(150 * 2048).SequenceEqual(payload),
            "data sectors are 2048-byte user data after a zero pregap");
    }

    private static void TestCdOverburnLimit()
    {
        SimulatedDrive drive = new() { Profile = MmcProfiles.CdR, AtipLeadOut = 296 };
        byte[] payload = new byte[300 * 2352];
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.CdAudio, payload.Length, (_, _) => { });
        string message = Throws<InvalidOperationException>(() => MmcCdSaoBurn.Burn(new MmcCommandSet(drive),
            new ByteStream(payload), request, CancellationToken.None));
        Require(message.Contains("超出盘片边界 4"), "more than 3 sectors past the ATIP lead-out is refused");
        Require(!drive.Log.Any(entry => entry.Cdb[0] is 0x2A or 0x5D) && drive.Opcodes()[^1] == 0x1E,
            "nothing is written and the medium is unlocked");
    }

    private static void TestDvdMinusRDiscAtOnce()
    {
        const int sectors = 100;
        SimulatedDrive drive = new() { Profile = MmcProfiles.DvdRSequential, FreeBlocks = 2_298_496 };
        byte[] payload = Pattern(sectors * 2048, 3);
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.DvdData, payload.Length, (_, _) => { },
            new OpticalWriteSpeed(2705, false));
        MmcDvdBurn.Burn(new MmcCommandSet(drive), MmcProfiles.DvdRSequential, new ByteStream(payload), request,
            CancellationToken.None);
        Require(drive.Opcodes().SequenceEqual(new byte[] { 0x1E, 0x51, 0x52, 0xB6, 0x5A, 0x55, 0x53 }
                .Concat(Enumerable.Repeat((byte)0x2A, 7)).Concat(new byte[] { 0x35, 0x00, 0x1E })),
            "DVD-R: lock, blank checks, speed, DAO page, reservation, writes, flush (no CLOSE), unlock");
        byte[] page = drive.DataOut(0x55)[8..];
        Require(page[2] == 0x42 && page[3] == 0x05 && page[4] == 0x08, "page 05h selects DAO with BUFE and 2048-byte blocks");
        Require(BinaryPrimitives.ReadUInt32BigEndian(drive.Cdb(0x53).AsSpan(5)) == 112, "the reservation is rounded up to whole ECC blocks");
        byte[] streaming = drive.DataOut(0xB6);
        Require(BinaryPrimitives.ReadUInt32BigEndian(streaming.AsSpan(20)) == 5540
                && BinaryPrimitives.ReadUInt32BigEndian(streaming.AsSpan(8)) == 2_298_495,
            "SET STREAMING asks for 5540 kB/s over the whole medium");
        byte[] written = drive.WrittenFrom(0);
        Require(written.Length == 112 * 2048 && written.AsSpan(0, payload.Length).SequenceEqual(payload)
                && written.AsSpan(payload.Length).IndexOfAnyExcept((byte)0) < 0,
            "the reserved track is filled: payload then zero padding");
    }

    private static void TestDvdPlusR()
    {
        const int sectors = 100;
        SimulatedDrive drive = new() { Profile = MmcProfiles.DvdPlusR, FreeBlocks = 2_295_104 };
        byte[] payload = Pattern(sectors * 2048, 5);
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.DvdData, payload.Length, (_, _) => { },
            new OpticalWriteSpeed(2705, false));
        MmcDvdBurn.Burn(new MmcCommandSet(drive), MmcProfiles.DvdPlusR, new ByteStream(payload), request,
            CancellationToken.None);
        Require(drive.Opcodes().SequenceEqual(new byte[] { 0x1E, 0x51, 0x52, 0xB6 }
                .Concat(Enumerable.Repeat((byte)0x2A, 7)).Concat(new byte[] { 0x35, 0x00, 0x5B, 0x00, 0x5B, 0x00, 0x1E })),
            "DVD+R: no write parameters or reservation; flush, close track, finalize, unlock");
        byte[][] closes = drive.Log.Where(entry => entry.Cdb[0] == 0x5B).Select(entry => entry.Cdb).ToArray();
        Require(closes[0][2] == 1 && closes[0][5] == 1 && closes[1][2] == 6, "CLOSE TRACK 1, then finalize (110b)");
        Require(drive.WrittenFrom(0).SequenceEqual(payload), "DVD+R writes exactly the payload");
    }

    private static void TestDvdCapacity()
    {
        SimulatedDrive drive = new() { Profile = MmcProfiles.DvdRSequential, FreeBlocks = 100 };
        byte[] payload = new byte[100 * 2048];
        OpticalBurnRequest request = new("/dev/sr0", OpticalBurnMediaKind.DvdData, payload.Length, (_, _) => { });
        string message = Throws<InvalidOperationException>(() => MmcDvdBurn.Burn(new MmcCommandSet(drive),
            MmcProfiles.DvdRSequential, new ByteStream(payload), request, CancellationToken.None));
        Require(message.Contains("剩余容量不足") && message.Contains("112"), "the rounded reservation is checked against free blocks");
        Require(!drive.Log.Any(entry => entry.Cdb[0] is 0x2A or 0x53) && drive.Opcodes()[^1] == 0x1E,
            "a refused burn writes nothing and unlocks");
    }

    private static void TestBluRaySessionSpeed()
    {
        SimulatedDrive drive = new() { Profile = MmcProfiles.BluRayRSequential };
        using MmcBluRaySequentialSession session = new(drive);
        session.Acquire();
        OpticalWriteSpeed speed = session.ConfigureWrite(new OpticalWriteSpeed(4390, false));
        session.Release();
        Require(speed.SectorsPerSecond == 4390, "the requested BD speed is reported");
        Require(drive.Opcodes().SequenceEqual(new byte[] { 0x1E, 0xB6, 0x1E }), "BD session: lock, SET STREAMING, unlock");
        byte[] streaming = drive.DataOut(0xB6);
        Require(BinaryPrimitives.ReadUInt32BigEndian(streaming.AsSpan(20)) == 8991
                && BinaryPrimitives.ReadUInt32BigEndian(streaming.AsSpan(8)) == OpticalBurnWorkflows.BluRaySingleLayerSectors - 1,
            "BD 2× is 8991 kB/s up to the last single-layer sector");
    }

    private static void TestBurnerEndToEnd()
    {
        const int sectors = 300;
        SimulatedDrive drive = new() { Profile = MmcProfiles.CdR, Speeds = [3528, 7056] };
        FakeProvider provider = new(drive);
        MmcOpticalDiscBurner burner = new(provider);
        IReadOnlyList<OpticalBurnDevice> devices = burner.GetDevicesAsync().GetAwaiter().GetResult();
        Require(devices.Count == 1 && devices[0].Id == "/dev/sr0" && devices[0].DisplayName.Contains("TEST"), "devices come from the provider");
        IReadOnlyList<OpticalWriteSpeed> speeds = burner.GetSupportedWriteSpeedsAsync("/dev/sr0", OpticalBurnMediaKind.CdAudio)
            .GetAwaiter().GetResult();
        Require(speeds.Select(speed => speed.SectorsPerSecond).SequenceEqual([1500, 3000]), "speeds are listed for the loaded CD");
        Require(provider.OpenCount == 1 && provider.Disposed == 1, "a speed query opens and releases the device");

        byte[] payload = Pattern(sectors * 2352, 13);
        List<OpticalBurnProgress> reports = [];
        OpticalBurnResult result = burner.BurnAsync(
            new OpticalBurnRequest("/dev/sr0", OpticalBurnMediaKind.CdAudio, payload.Length,
                (stream, _) => stream.Write(payload)),
            new SynchronousProgress(reports.Add)).GetAwaiter().GetResult();
        Require(result.BytesWritten == payload.Length && result.ActualWriteSpeed!.SectorsPerSecond == 1500,
            "the burn reports the slowest speed it selected");
        Require(drive.WrittenFrom(-150).AsSpan(150 * 2352).SequenceEqual(payload), "the generated stream reaches the drive");
        Require(reports[^1].Stage == OpticalBurnStages.Complete
                && reports.Any(report => report.Stage == OpticalBurnStages.Write && report.CompletedBytes == payload.Length),
            "progress runs through accepted writes to completion");
        Require(provider.Disposed == 2, "the burn releases the device");

        drive.Profile = MmcProfiles.DvdPlusR;
        Exception failure = Catch(() => burner.BurnAsync(new OpticalBurnRequest("/dev/sr0", OpticalBurnMediaKind.CdAudio,
            payload.Length, (stream, _) => stream.Write(payload))).GetAwaiter().GetResult());
        Require(failure is InvalidOperationException && provider.Disposed == 3, "a wrong medium fails cleanly and releases the device");
    }

    private static void TestLinuxDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "mmc-sysfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            string block = Path.Combine(root, "sys", "class", "block");
            WriteFile(Path.Combine(block, "sr0", "dev"), "11:0\n");
            WriteFile(Path.Combine(block, "sr0", "device", "vendor"), "PIONEER \n");
            WriteFile(Path.Combine(block, "sr0", "device", "model"), "BD-RW   BDR-XD08\n");
            WriteFile(Path.Combine(block, "sr0", "queue", "max_sectors_kb"), "120\n");
            WriteFile(Path.Combine(block, "sr1", "dev"), "11:1\n");
            WriteFile(Path.Combine(block, "sda", "dev"), "8:0\n");
            WriteFile(Path.Combine(block, "srx", "dev"), "1:2\n");
            string mountInfo = Path.Combine(root, "mountinfo");
            WriteFile(mountInfo,
                "22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw\n"
                + "99 22 11:1 / /media/user/My\\040Disc ro,nosuid shared:50 - udf /dev/sr1 ro\n");
            LinuxOpticalDeviceProvider provider = new(block, mountInfo, "/dev");
            IReadOnlyList<MmcDeviceInfo> devices = provider.EnumerateDevices();
            Require(devices.Count == 2 && devices[0].Id == "/dev/sr0" && devices[1].Id == "/dev/sr1",
                "only srN block devices are recorders");
            Require(devices[0].DisplayName == "PIONEER BD-RW   BDR-XD08 (/dev/sr0)" && devices[0].MountPoints.Count == 0,
                "the display name joins sysfs vendor and model");
            Require(devices[1].DisplayName == "光驱 (/dev/sr1)" && devices[1].MountPoints.SequenceEqual(["/media/user/My Disc"]),
                "mounts are matched by major:minor and unescaped");
            Require(provider.ReadTransferLimit("/dev/sr0") == 120 * 1024 && provider.ReadTransferLimit("/dev/sr1") == 64 * 1024,
                "max_sectors_kb bounds a command, with a 64 KiB default");
            Require(Throws<InvalidOperationException>(() => provider.Open("/dev/sr1")).Contains("umount /media/user/My Disc"),
                "a mounted medium is refused with the unmount hint");
            Require(Throws<InvalidOperationException>(() => provider.Open("/dev/sda")).Contains("找不到光驱"),
                "only enumerated recorders can be opened");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static void WriteFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private static void TestMacLayout()
    {
        Require(Marshal.SizeOf<CfUuidBytes>() == 16 && Marshal.SizeOf<IoVirtualRange>() == 16,
            "CFUUIDBytes and 64-bit IOVirtualRange are 16 bytes");
        CfUuidBytes mmc = CfUuidBytes.From(MacNative.MmcDeviceUserClientTypeId);
        Require(mmc.B0 == 0x97 && mmc.B1 == 0xAB && mmc.B3 == 0x2C && mmc.B6 == 0x11 && mmc.B7 == 0xD5 && mmc.B15 == 0x66,
            "plug-in UUIDs are passed in CFUUIDBytes (big-endian) order");
        Require(MacNative.DescribeExclusiveFailure("TEST", MacNative.ReturnBusy).Message.Contains("diskutil unmount"),
            "a busy drive explains how to unmount");
    }

    private static byte[] Pattern(int length, int seed)
    {
        byte[] bytes = new byte[length];
        for (int index = 0; index < length; index++) bytes[index] = (byte)(index * 31 + seed + index / 2352);
        return bytes;
    }

    private static string Throws<TException>(Action action) where TException : Exception
    {
        Exception exception = Catch(action);
        Require(exception is TException, $"expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}");
        return exception.Message;
    }

    private static Exception Catch(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception is AggregateException { InnerException: { } inner } ? inner : exception; }
        throw new InvalidOperationException("MMC backend: expected an exception.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("MMC backend: " + message);
    }

    private sealed class SynchronousProgress(Action<OpticalBurnProgress> report) : IProgress<OpticalBurnProgress>
    {
        public void Report(OpticalBurnProgress value) => report(value);
    }

    private sealed class FakeProvider(SimulatedDrive drive) : IMmcDeviceProvider
    {
        internal int OpenCount { get; private set; }
        internal int Disposed { get; private set; }

        public IReadOnlyList<MmcDeviceInfo> EnumerateDevices() => [new("/dev/sr0", "TEST DRIVE (/dev/sr0)", [])];

        public IMmcDevice Open(string deviceId)
        {
            Require(deviceId == "/dev/sr0", "the burner opens the requested device");
            OpenCount++;
            return new Device(drive, () => Disposed++);
        }

        private sealed class Device(SimulatedDrive drive, Action dispose) : IMmcDevice
        {
            public IMmcCommandTransport Transport => drive;
            public void Dispose() => dispose();
        }
    }

    /// <summary>An in-memory IStream over a byte array (forward reads only).</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ByteStream(byte[] bytes) : IStream
    {
        private int _position;
        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int count = Math.Min(cb, bytes.Length - _position);
            Buffer.BlockCopy(bytes, _position, pv, 0, count);
            _position += count;
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, count);
        }
        public void Stat(out STATSTG stat, int flags) => stat = new() { type = 2, cbSize = bytes.Length };
        public void Seek(long offset, int origin, IntPtr newPosition) => throw new NotSupportedException();
        public void Write(byte[] p, int c, IntPtr n) => throw new NotSupportedException();
        public void SetSize(long size) => throw new NotSupportedException();
        public void CopyTo(IStream stream, long count, IntPtr read, IntPtr written) => throw new NotSupportedException();
        public void Commit(int flags) { }
        public void Revert() => throw new NotSupportedException();
        public void LockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void UnlockRegion(long offset, long count, int type) => throw new NotSupportedException();
        public void Clone(out IStream stream) => throw new NotSupportedException();
    }

    /// <summary>A blank recorder that answers the MMC queries the backend issues and records
    /// every command with its data-out payload.</summary>
    private sealed class SimulatedDrive : IMmcCommandTransport
    {
        internal int Profile { get; set; }
        internal uint FreeBlocks { get; init; } = 2_295_104;
        internal long AtipLeadOut { get; init; } = 359_849;
        internal uint[] Speeds { get; init; } = [];
        internal int BusyWrites { get; set; }
        internal int IllegalWrites { get; set; }
        internal List<(byte[] Cdb, byte[] Data)> Log { get; } = [];

        internal byte[] Opcodes() => Log.Select(entry => entry.Cdb[0]).ToArray();
        internal byte[] Cdb(byte opcode) => Log.Single(entry => entry.Cdb[0] == opcode).Cdb;
        internal byte[] DataOut(byte opcode) => Log.Single(entry => entry.Cdb[0] == opcode).Data;

        /// <summary>All WRITE payloads concatenated, checking that LBAs are consecutive from start.</summary>
        internal byte[] WrittenFrom(long start)
        {
            using MemoryStream bytes = new();
            long next = start;
            foreach ((byte[] cdb, byte[] data) in Log.Where(entry => entry.Cdb[0] == 0x2A))
            {
                Require(BinaryPrimitives.ReadInt32BigEndian(cdb.AsSpan(2)) == next, "WRITE LBAs are consecutive");
                int sectors = BinaryPrimitives.ReadUInt16BigEndian(cdb.AsSpan(7));
                Require(data.Length % sectors == 0, "WRITE length is whole sectors");
                next += sectors;
                bytes.Write(data);
            }

            return bytes.ToArray();
        }

        public MmcWriteTransferLimits GetWriteTransferLimits() => new(0, 1024 * 1024);

        public MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds)
        {
            Log.Add((cdb.ToArray(), []));
            byte[] data = cdb[0] switch
            {
                0x46 => Configuration(),
                0x51 => DiscInformation(),
                0x52 => TrackInformation(),
                0x43 => Atip(),
                0xAC => Performance(),
                0x5A => WriteParametersPage(),
                _ => [],
            };
            return MmcCommandResult.Success(data.AsSpan(0, Math.Min(data.Length, allocationLength)).ToArray());
        }

        public MmcCommandResult SendData(byte[] cdb, IntPtr buffer, int bufferLength, uint timeoutSeconds)
        {
            byte[] data = new byte[bufferLength];
            Marshal.Copy(buffer, data, 0, bufferLength);
            Log.Add((cdb.ToArray(), data));
            if (cdb[0] == 0x2A && BusyWrites > 0)
            {
                BusyWrites--;
                Log.RemoveAt(Log.Count - 1);
                Log.Add((cdb.ToArray(), []));
                return MmcCommandResult.CheckCondition([0x70, 0, 0x02, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0x04, 0x08]);
            }

            if (cdb[0] == 0x2A && IllegalWrites > 0)
            {
                IllegalWrites--;
                return MmcCommandResult.CheckCondition([0x70, 0, 0x05, 0, 0, 0, 0, 10, 0, 0, 0, 0, 0x24, 0x00]);
            }

            return MmcCommandResult.Success([]);
        }

        private byte[] Configuration()
        {
            byte[] data = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(data, 4);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), (ushort)Profile);
            return data;
        }

        private static byte[] DiscInformation()
        {
            byte[] data = new byte[34];
            BinaryPrimitives.WriteUInt16BigEndian(data, 32);
            data[2] = 0;  // blank
            data[3] = 1;
            data[6] = 1;
            data[21] = 79; data[22] = 59; data[23] = 74;
            return data;
        }

        private byte[] TrackInformation()
        {
            byte[] data = new byte[48];
            BinaryPrimitives.WriteUInt16BigEndian(data, 46);
            data[2] = 1;
            data[6] = 0x40;  // blank
            data[7] = 0x01;  // NWA valid
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), FreeBlocks);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), FreeBlocks);
            return data;
        }

        private byte[] Atip()
        {
            byte[] data = new byte[28];
            BinaryPrimitives.WriteUInt16BigEndian(data, 26);
            data[8] = 97; data[9] = 26; data[10] = 66;
            long frames = AtipLeadOut + 150;
            data[12] = (byte)(frames / 4500); data[13] = (byte)(frames / 75 % 60); data[14] = (byte)(frames % 75);
            return data;
        }

        private byte[] Performance()
        {
            byte[] data = new byte[8 + Speeds.Length * 16];
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)(data.Length - 4));
            for (int index = 0; index < Speeds.Length; index++)
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8 + index * 16 + 12), Speeds[index]);
            return data;
        }

        private static byte[] WriteParametersPage()
        {
            byte[] data = new byte[8 + 52];
            BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)(data.Length - 2));
            data[8] = 0x85;  // PS + page 05h
            data[9] = 50;
            data[10] = 0x01; // whatever the drive had: TAO
            data[16] = 0xFF; // stale MCN byte
            return data;
        }
    }
}
