using System.Runtime.InteropServices.ComTypes;

namespace DiscImageStudio.Burning;

/// <summary>
/// CD audio and CD data through session-at-once with a cue sheet (MMC-6 6.33). The drive
/// generates the lead-in TOC, P/Q subcode and lead-out; the host sends the 150-sector pregap
/// and the payload from LBA -150. Audio is 2352-byte little-endian CD-DA (data form 00h). Data
/// is 2048-byte Mode 1 (data form 10h): the drive adds sync, header, EDC/ECC and scrambling,
/// which is the same physical sector the Windows path encodes on the host.
/// </summary>
internal static class MmcCdSaoBurn
{
    internal const int PregapSectors = 150;
    internal const int AudioSectorsPerWrite = 27;  // 63,504 bytes
    internal const int DataSectorsPerWrite = 32;   // 65,536 bytes

    internal static OpticalWriteSpeed Burn(MmcCommandSet commands, IStream content, OpticalBurnRequest request,
        CancellationToken cancellationToken, Action<string>? reportOperation = null,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null, Action<long, long>? reportPayloadAccepted = null,
        Action<string>? reportCleanupWarning = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(content);
        request.Validate();
        if (!OpticalBurnMedia.UsesCdDao(request.MediaKind))
            throw new ArgumentException("CD SAO 只接受音轨或 CD 数据。", nameof(request));
        bool data = request.MediaKind == OpticalBurnMediaKind.CdData;
        int sectorBytes = data ? 2048 : 2352;
        long sectors = request.ContentLength / sectorBytes;
        string trackDescription = data ? "数据轨" : "音轨";

        return MmcBurnLock.Run(commands, cancellationToken, reportCleanupWarning, (cancel, markWriteStarted) =>
        {
            reportOperation?.Invoke("检查 CD 是否为空白盘");
            MmcDiscInformation disc = commands.ReadDiscInformation();
            if (!disc.IsBlank) throw new InvalidOperationException("CD 不是空白盘，已取消刻录。");
            long lastLeadOut = commands.ReadAtipLastPossibleLeadOut() ?? disc.LastPossibleLeadOutStartLba;
            long excessSectors = sectors - lastLeadOut;
            if (excessSectors > OpticalBurnWorkflows.CdDaoOverburnAllowanceSectors)
                throw new InvalidOperationException(
                    $"CD lead-out 超出盘片边界 {excessSectors} 个扇区；"
                    + $"最多允许 {OpticalBurnWorkflows.CdDaoOverburnAllowanceSectors} 个扇区的受限 overburn。");
            if (excessSectors > 0)
                reportOperation?.Invoke($"使用 CD 末端 overburn（超出 {excessSectors} 个扇区）");

            reportOperation?.Invoke("设置 CD SAO 写入参数");
            byte[] page = commands.ReadWriteParametersPage();
            ConfigureWriteParameters(page, data);
            commands.WriteWriteParametersPage(page);

            reportOperation?.Invoke("设置 CD 刻录速度");
            OpticalWriteSpeed speed = MmcSpeedControl.Apply(commands, request.MediaKind,
                request.WriteSpeed ?? new OpticalWriteSpeed(OpticalBurnWorkflows.CdDaoFallbackSpeedSectorsPerSecond, false),
                checked((uint)Math.Max(0, lastLeadOut - 1)), reportOperation);
            reportWriteSpeed?.Invoke(speed);

            reportOperation?.Invoke("发送 CD 曲目表（cue sheet）");
            commands.SendCueSheet(BuildCueSheet(sectors, data));

            cancellationToken.ThrowIfCancellationRequested();
            reportOperation?.Invoke($"写入 CD SAO {trackDescription}（先写导入区，请等待）");
            markWriteStarted();
            MmcSequentialWriter.Write(commands.Transport, content,
                new MmcWritePlan(-PregapSectors, sectorBytes, data ? DataSectorsPerWrite : AudioSectorsPerWrite,
                    PregapSectors, sectors, 0),
                reportPayloadAccepted, cancel);
            cancellationToken.ThrowIfCancellationRequested();

            // SAO needs no CLOSE: flushing makes the drive write the lead-out and finish.
            reportOperation?.Invoke("刷新缓存并写入 CD 导出区（请等待完成）");
            commands.SynchronizeCache();
            return speed;
        });
    }

    /// <summary>Write parameters page 05h for SAO: buffer-underrun protection on, no test write,
    /// single session, CD-DA/CD-ROM session format, 2-second audio pause.</summary>
    internal static void ConfigureWriteParameters(Span<byte> page, bool data)
    {
        if (page.Length < 16) throw new ArgumentException("Write parameters page is too short.", nameof(page));
        page[2] = 0x40 | 0x02;                 // BUFE, write type 2 (SAO)
        page[3] = (byte)(data ? 0x04 : 0x00);  // no multisession, no FP/copy, track mode
        page[4] = (byte)(data ? 0x08 : 0x00);  // data block type: Mode 1 / raw 2352
        page[5] = 0;                           // link size
        page[7] = 0;                           // host application code
        page[8] = 0;                           // session format: CD-DA or CD-ROM
        page[10] = page[11] = page[12] = page[13] = 0;
        page[14] = 0;
        page[15] = PregapSectors;              // audio pause length
        if (page.Length >= 48) page[16..48].Clear();  // no MCN / ISRC
    }

    /// <summary>One-track cue sheet: lead-in, pregap (index 0) and program (index 1) from the
    /// host, lead-out generated by the drive. MSF is absolute: 00:02:00 is LBA 0.</summary>
    internal static byte[] BuildCueSheet(long programSectors, bool data)
    {
        if (programSectors <= 0) throw new ArgumentOutOfRangeException(nameof(programSectors));
        byte controlAdr = (byte)(data ? 0x41 : 0x01);   // data track / 2-channel audio, copy prohibited; ADR 1
        byte hostForm = (byte)(data ? 0x10 : 0x00);     // Mode 1 2048 / CD-DA 2352 from the host
        byte generatedForm = (byte)(data ? 0x14 : 0x01); // generated by the drive
        (byte minute, byte second, byte frame) = MmcCommandSet.LbaToMsf(programSectors);
        return
        [
            controlAdr, 0x00, 0x00, generatedForm, 0, 0, 0, 0,
            controlAdr, 0x01, 0x00, hostForm, 0, 0, 0, 0,
            controlAdr, 0x01, 0x01, hostForm, 0, 0, 2, 0,
            controlAdr, 0xAA, 0x01, generatedForm, 0, minute, second, frame,
        ];
    }
}

/// <summary>
/// Blank DVD-R / DVD-R DL in disc-at-once (reserved track, the drive closes the disc after the
/// cache flush) and blank DVD+R / DVD+R DL (sequential write, close track, finalize). Rewritable
/// DVDs are not offered by the MMC backends.
/// </summary>
internal static class MmcDvdBurn
{
    internal const int SectorsPerWrite = 16;   // one 32 KiB ECC block
    internal const int EccBlockSectors = 16;

    internal static OpticalWriteSpeed Burn(MmcCommandSet commands, int profile, IStream content,
        OpticalBurnRequest request, CancellationToken cancellationToken, Action<string>? reportOperation = null,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null, Action<long, long>? reportPayloadAccepted = null,
        Action<string>? reportCleanupWarning = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(content);
        request.Validate();
        if (request.MediaKind != OpticalBurnMediaKind.DvdData)
            throw new ArgumentException("DVD 通道只接受 DVD 数据。", nameof(request));
        bool minusR = MmcProfiles.IsDvdMinusR(profile);
        if (!minusR && !MmcProfiles.IsDvdPlusR(profile))
            throw new InvalidOperationException(MmcMediaPolicy.UnsupportedDvdMessage(profile));
        long sectors = request.ContentLength / 2048;
        // DVD-R DAO reserves whole ECC blocks and must then be written completely.
        long reserved = minusR ? (sectors + EccBlockSectors - 1) / EccBlockSectors * EccBlockSectors : sectors;

        return MmcBurnLock.Run(commands, cancellationToken, reportCleanupWarning, (cancel, markWriteStarted) =>
        {
            reportOperation?.Invoke("检查 DVD 是否为空白盘");
            if (!commands.ReadDiscInformation().IsBlank)
                throw new InvalidOperationException("DVD 不是空白盘，已取消刻录。");
            MmcTrackInformation track = commands.ReadTrackInformation(1);
            if (!track.Blank || !track.NextWritableAddressValid || track.NextWritableAddress != 0 || track.TrackStart != 0)
                throw new InvalidOperationException("DVD 绘图需要从 LBA 0 开始的全新空白盘，已取消刻录。");
            if (track.FreeBlocks < reserved)
            {
                long deficit = reserved - track.FreeBlocks;
                throw new InvalidOperationException(
                    $"DVD 剩余容量不足：需要 {reserved:N0} 扇区（{reserved / 512.0:F3} MiB），"
                    + $"实际可写 {track.FreeBlocks:N0} 扇区（{track.FreeBlocks / 512.0:F3} MiB），"
                    + $"超出 {deficit:N0} 扇区（{deficit / 512.0:F3} MiB）。");
            }

            reportOperation?.Invoke("设置 DVD 刻录速度");
            OpticalWriteSpeed? requested = request.WriteSpeed;
            OpticalWriteSpeed speed = requested is null
                ? MmcSpeedControl.DriveDefault(commands, request.MediaKind)
                : MmcSpeedControl.Apply(commands, request.MediaKind, requested, Math.Max(1, track.FreeBlocks) - 1, reportOperation);
            reportWriteSpeed?.Invoke(speed);

            if (minusR)
            {
                reportOperation?.Invoke("设置 DVD-R 一次写入（DAO）参数");
                byte[] page = commands.ReadWriteParametersPage();
                ConfigureDiscAtOnce(page);
                commands.WriteWriteParametersPage(page);
                reportOperation?.Invoke($"预留 DVD-R 轨道：{reserved:N0} 扇区");
                commands.ReserveTrack(checked((uint)reserved));
            }

            cancellationToken.ThrowIfCancellationRequested();
            reportOperation?.Invoke($"写入 DVD 数据：{sectors:N0} 扇区");
            markWriteStarted();
            MmcSequentialWriter.Write(commands.Transport, content,
                new MmcWritePlan(0, 2048, SectorsPerWrite, 0, sectors, reserved - sectors),
                reportPayloadAccepted, cancel);
            cancellationToken.ThrowIfCancellationRequested();

            reportOperation?.Invoke("刷新 DVD 缓存并封盘（请等待完成）");
            commands.SynchronizeCache();
            if (!minusR)
            {
                commands.Close(function: 1, trackNumber: 1);
                commands.Close(function: 6);
            }

            return speed;
        });
    }

    /// <summary>Write parameters page 05h for DVD-R disc-at-once.</summary>
    internal static void ConfigureDiscAtOnce(Span<byte> page)
    {
        if (page.Length < 16) throw new ArgumentException("Write parameters page is too short.", nameof(page));
        page[2] = 0x40 | 0x02;  // BUFE, write type 2 (DAO)
        page[3] = 0x05;         // single session, track mode 5
        page[4] = 0x08;         // data block type: 2048-byte user data
        page[5] = 0;
        page[7] = 0;
        page[8] = 0;
        page[10] = page[11] = page[12] = page[13] = 0;
        page[14] = page[15] = 0;
    }
}

/// <summary>
/// Locks the medium for one burn and guarantees the unlock. When a write fails after it
/// started, the cache is still flushed so the drive leaves its busy state, but nothing is
/// closed or finalized on that path.
/// </summary>
internal static class MmcBurnLock
{
    internal static T Run<T>(MmcCommandSet commands, CancellationToken cancellationToken, Action<string>? reportCleanupWarning,
        Func<Func<bool>, Action, T> burn)
    {
        int cancelRequested = 0;
        bool writeStarted = false;
        commands.PreventRemoval(true);
        try
        {
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => Interlocked.Exchange(ref cancelRequested, 1));
            T result = burn(() => Volatile.Read(ref cancelRequested) != 0, () => writeStarted = true);
            Unlock(commands, reportCleanupWarning);
            return result;
        }
        catch (Exception exception)
        {
            if (writeStarted)
            {
                try { commands.SynchronizeCache(); }
                catch (Exception flush) { Warn(reportCleanupWarning, $"写入失败后刷新缓存失败：{flush.Message}"); }
            }

            Unlock(commands, reportCleanupWarning);
            if (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
                throw new OperationCanceledException("刻录已取消。", exception, cancellationToken);
            throw;
        }
    }

    private static void Unlock(MmcCommandSet commands, Action<string>? reportCleanupWarning)
    {
        try { commands.PreventRemoval(false); }
        catch (Exception exception) { Warn(reportCleanupWarning, $"解除盘片弹出保护失败：{exception.Message}"); }
    }

    private static void Warn(Action<string>? report, string message)
    {
        try { report?.Invoke(message); }
        catch { /* Diagnostics must not replace the burn outcome. */ }
    }
}

/// <summary>Write speed descriptors in the backends' sectors/second convention (CD: 2352-byte
/// physical sectors, 75/s at 1×; DVD and BD: 2048-byte sectors) and kB/s for the drive.</summary>
internal static class MmcSpeedControl
{
    internal static int SectorBytes(OpticalBurnMediaKind kind) => OpticalBurnMedia.UsesCdDao(kind) ? 2352 : 2048;

    internal static IReadOnlyList<OpticalWriteSpeed> ToSpeeds(IEnumerable<MmcWriteSpeedDescriptor> descriptors,
        OpticalBurnMediaKind kind)
    {
        int sectorBytes = SectorBytes(kind);
        return descriptors
            .Select(descriptor => new OpticalWriteSpeed(
                (int)Math.Round(descriptor.WriteKilobytesPerSecond * 1000.0 / sectorBytes),
                descriptor.WriteRotationControl == 1))
            .Where(speed => speed.SectorsPerSecond > 0)
            .Distinct()
            .OrderBy(speed => speed.SectorsPerSecond)
            .ThenBy(speed => speed.RotationTypeIsPureCav)
            .ToArray();
    }

    internal static uint ToKilobytesPerSecond(OpticalWriteSpeed speed, OpticalBurnMediaKind kind)
        => (uint)Math.Max(1, Math.Round(speed.SectorsPerSecond * (double)SectorBytes(kind) / 1000.0));

    /// <summary>Sets the requested speed (SET STREAMING, or SET CD SPEED for CDs and as the
    /// fallback). A drive that refuses both keeps its own default; the result says so.</summary>
    internal static OpticalWriteSpeed Apply(MmcCommandSet commands, OpticalBurnMediaKind kind, OpticalWriteSpeed speed,
        uint endLba, Action<string>? reportOperation)
    {
        speed.Validate();
        uint kilobytes = ToKilobytesPerSecond(speed, kind);
        bool cd = OpticalBurnMedia.UsesCdDao(kind);
        Action first = cd ? () => commands.SetCdSpeed(kilobytes) : () => commands.SetStreaming(kilobytes, endLba, speed.RotationTypeIsPureCav);
        Action second = cd ? () => commands.SetStreaming(kilobytes, endLba, speed.RotationTypeIsPureCav) : () => commands.SetCdSpeed(kilobytes);
        try
        {
            first();
        }
        catch (MmcCommandException)
        {
            try
            {
                second();
            }
            catch (MmcCommandException exception)
            {
                reportOperation?.Invoke($"光驱拒绝设置写入速度，将使用光驱默认速度：{exception.Message}");
            }
        }

        return speed;
    }

    /// <summary>The slowest reported speed, or 1× when the drive reports none (shown as such).</summary>
    internal static OpticalWriteSpeed DriveDefault(MmcCommandSet commands, OpticalBurnMediaKind kind)
    {
        OpticalWriteSpeed? slowest = ToSpeeds(commands.GetWriteSpeeds(), kind).FirstOrDefault();
        return slowest ?? kind switch
        {
            OpticalBurnMediaKind.DvdData => new OpticalWriteSpeed(676, false),
            OpticalBurnMediaKind.BluRayData => new OpticalWriteSpeed(2195, false),
            _ => new OpticalWriteSpeed(75, false),
        };
    }
}

/// <summary>Which loaded media the MMC backends write, with actionable messages otherwise.</summary>
internal static class MmcMediaPolicy
{
    internal static void Require(OpticalBurnMediaKind kind, int profile)
    {
        switch (kind)
        {
            case OpticalBurnMediaKind.CdAudio or OpticalBurnMediaKind.CdData:
                if (profile is MmcProfiles.CdR or MmcProfiles.CdRw) return;
                throw new InvalidOperationException(
                    $"所选刻录模式需要空白 CD-R 或 CD-RW；当前盘片为 {MmcProfiles.Describe(profile)}。");
            case OpticalBurnMediaKind.DvdData:
                if (MmcProfiles.IsDvdMinusR(profile) || MmcProfiles.IsDvdPlusR(profile)) return;
                throw new InvalidOperationException(UnsupportedDvdMessage(profile));
            case OpticalBurnMediaKind.BluRayData:
                if (profile == MmcProfiles.BluRayRSequential) return;
                throw new InvalidOperationException(
                    $"无备用区绘图需要顺序记录 BD-R（SRM）；当前盘片为 {MmcProfiles.Describe(profile)}。");
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    internal static string UnsupportedDvdMessage(int profile) => profile is MmcProfiles.DvdRam
        or MmcProfiles.DvdRwRestricted or MmcProfiles.DvdRwSequential or MmcProfiles.DvdPlusRw
        or MmcProfiles.DvdPlusRwDualLayer or MmcProfiles.DvdRDualLayerJump
        ? $"Linux / macOS 版暂不支持在 {MmcProfiles.Describe(profile)} 上刻录（可擦写或跳层记录 DVD）；请使用空白 DVD-R 或 DVD+R（含双层）。"
        : $"所选刻录模式需要空白 DVD-R 或 DVD+R；当前盘片为 {MmcProfiles.Describe(profile)}。";
}
