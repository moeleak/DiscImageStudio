using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using static DiscImageStudio.Burning.OpticalBurnWorkflows;

namespace DiscImageStudio.Burning;

/// <summary>Windows backend: device access, media checks and DVD writes through IMAPI2.
/// Burn orchestration shared with the MMC backends lives in <see cref="OpticalBurnWorkflows"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsImapiBurner : IOpticalDiscBurner
{
    private readonly OpticalBurnOperationGate _operationGate = new();

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
        => _operationGate.RunAsync(() => RunOnStaThread(() => EnumerateDevices(cancellationToken)),
            cancellationToken);

    public Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(
        string deviceId,
        OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        _ = OpticalBurnMedia.GetSectorBytes(mediaKind);
        return _operationGate.RunAsync(() => RunOnStaThread(() => EnumerateWriteSpeeds(
            deviceId,
            mediaKind,
            cancellationToken)), cancellationToken);
    }

    public Task<OpticalBurnResult> BurnAsync(
        OpticalBurnRequest request,
        IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return _operationGate.RunAsync(() => RunOnStaThread(() => Burn(request, progress, cancellationToken)),
            cancellationToken);
    }

    private static IReadOnlyList<OpticalBurnDevice> EnumerateDevices(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dynamic master = CreateComObject("IMAPI2.MsftDiscMaster2");
        try
        {
            if (!(bool)master.IsSupportedEnvironment)
            {
                return [];
            }

            int count = (int)master.Count;
            List<OpticalBurnDevice> devices = new(count);
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = (string)master.Item(index);
                dynamic recorder = CreateRecorder(id);
                try
                {
                    string vendor = Convert.ToString(recorder.VendorId)?.Trim() ?? string.Empty;
                    string product = Convert.ToString(recorder.ProductId)?.Trim() ?? string.Empty;
                    IReadOnlyList<string> volumes = ReadStrings(recorder.VolumePathNames);
                    string drive = volumes.Count == 0 ? string.Empty : $" ({string.Join(", ", volumes)})";
                    string model = string.Join(" ", new[] { vendor, product }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                    devices.Add(new OpticalBurnDevice(
                        id,
                        (model.Length == 0 ? $"光驱 {index + 1}" : model) + drive,
                        volumes));
                }
                finally
                {
                    ReleaseComObject(recorder);
                }
            }

            return devices;
        }
        finally
        {
            ReleaseComObject(master);
        }
    }

    private static IReadOnlyList<OpticalWriteSpeed> EnumerateWriteSpeeds(
        string deviceId,
        OpticalBurnMediaKind mediaKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dynamic recorder = CreateRecorder(deviceId);
        dynamic? format = null;
        try
        {
            format = OpticalBurnMedia.UsesCdDao(mediaKind)
                ? CreateComObject("IMAPI2.MsftDiscFormat2RawCD")
                : CreateComObject("IMAPI2.MsftDiscFormat2Data");
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            if (!(bool)format.IsRecorderSupported(recorder))
            {
                throw new InvalidOperationException("所选光驱不支持这种刻录方式。");
            }

            if (!(bool)format.IsCurrentMediaSupported(recorder))
            {
                throw new InvalidOperationException("请放入与所选 CD/DVD/蓝光模式兼容的可写盘片。");
            }

            ValidateRequestedMedia(mediaKind, recorder, format);
            cancellationToken.ThrowIfCancellationRequested();
            return ReadSupportedWriteSpeeds(format);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"读取当前盘片的刻录速度失败（0x{exception.HResult:X8}）：{exception.Message}",
                exception);
        }
        finally
        {
            if (format is not null)
            {
                ReleaseComObject(format);
            }

            ReleaseComObject(recorder);
        }
    }

    private static OpticalBurnResult Burn(
        OpticalBurnRequest request,
        IProgress<OpticalBurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ImapiWriteProgressReporter writeProgress = new(request.MediaKind, request.ContentLength,
            value => progress?.Report(value));
        string currentOperation = "初始化 Windows 刻录接口";
        List<string> cleanupWarnings = [];
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OpticalBurnProgress(
            "device",
            "正在检查刻录机和空白介质…",
            0,
            request.ContentLength));
        dynamic recorder = CreateRecorder(request.DeviceId);
        dynamic? format = null;
        OpticalBurnResult? result = null;
        Exception? burnFailure = null;
        try
        {
            currentOperation = "创建刻录格式";
            format = OpticalBurnMedia.UsesCdDao(request.MediaKind)
                ? CreateComObject("IMAPI2.MsftDiscFormat2RawCD")
                : CreateComObject("IMAPI2.MsftDiscFormat2Data");
            currentOperation = "连接刻录机";
            format.Recorder = recorder;
            format.ClientName = "Disc Image Studio";
            currentOperation = "检查刻录机支持";
            if (!(bool)format.IsRecorderSupported(recorder))
            {
                throw new InvalidOperationException("所选光驱不支持这种刻录方式。");
            }

            currentOperation = "检查当前盘片兼容性";
            if (!(bool)format.IsCurrentMediaSupported(recorder))
            {
                throw new InvalidOperationException("当前盘片与所选 CD/DVD/蓝光模式不兼容。");
            }

            ValidateRequestedMedia(request.MediaKind, recorder, format);
            if (request.WriteSpeed is null)
            {
                currentOperation = "选择最低刻录速度";
                request = request with
                {
                    WriteSpeed = ResolveWriteSpeed(
                        null,
                        ReadSupportedWriteSpeeds(format)),
                };
                progress?.Report(new OpticalBurnProgress(
                    "speed",
                    request.WriteSpeed is null
                        ? "当前盘片未报告可选速度，将使用刻录接口的保守回退设置…"
                        : $"自动选择最低写入速度："
                            + $"{request.WriteSpeed.GetMultiplier(request.MediaKind):0.#}×"
                            + $"（{request.WriteSpeed.GetMegabytesPerSecond(request.MediaKind):0.0} MB/s）…",
                    0,
                    request.ContentLength));
            }

            using GeneratedContentComStream content = new(
                request.ContentLength,
                request.ProduceContent,
                (completed, total) => progress?.Report(new OpticalBurnProgress(
                    "transfer",
                    $"正在向刻录接口传输 {completed / (1024.0 * 1024.0):F1} / {total / (1024.0 * 1024.0):F1} MiB…",
                    completed,
                    total)),
                cancellationToken,
                request.EffectiveBufferOptions);
            progress?.Report(new OpticalBurnProgress(
                "buffer",
                "正在准备防断流缓冲区…",
                0,
                request.ContentLength));
            content.WaitUntilPrebuffered(cancellationToken);
            // Preparation may take time. Recheck the loaded medium before writing.
            ValidateRequestedMedia(request.MediaKind, recorder, format);

            OpticalWriteSpeed? actualWriteSpeed;
            if (OpticalBurnMedia.UsesCdDao(request.MediaKind))
            {
                using ICdRawImageSession imageSession = request.MediaKind == OpticalBurnMediaKind.CdData
                    ? new CdDataRawImageSession() : new DynamicCdRawImageSession();
                actualWriteSpeed = BurnCdDao(
                    new DynamicCdRawSession((object)format, writeProgress.ReportRawCdUpdate),
                    imageSession,
                    content,
                    request,
                    cancellationToken,
                    operation =>
                    {
                        currentOperation = operation;
                        if (operation == "关闭 CD 刻录会话")
                            writeProgress.ReportStage("finalize", "正在关闭 CD 刻录会话…");
                    },
                    speed => ReportWriteSpeed(progress, speed, request));
            }
            else if (request.MediaKind == OpticalBurnMediaKind.BluRayData)
            {
                // IDiscFormat2Data.Write formats virgin BD-R with spare areas.
                // The lower-level write engine only sends WRITE commands, so
                // the unformatted track keeps the LBA/physical-address mapping.
                using DynamicBluRaySequentialSession session = new((object)recorder, (object)format,
                    writeProgress.ReportAcceptedBytes,
                    reportCleanupWarning: message => cleanupWarnings.Add(message),
                    reportDiagnostics: message => progress?.Report(new OpticalBurnProgress(
                        "diagnostic", message, 0, request.ContentLength)));
                actualWriteSpeed = BurnBluRay(session, content, request,
                    cancellationToken, operation =>
                    {
                        currentOperation = operation;
                        bool finalizing = operation.Contains("封盘", StringComparison.Ordinal);
                        writeProgress.ReportStage(finalizing ? "finalize" : "media", operation);
                    },
                    speed => ReportWriteSpeed(progress, speed, request),
                    reportCleanupWarning: cleanupWarnings.Add);
            }
            else
            {
                currentOperation = "写入 DVD 数据";
                Action<OpticalWriteSpeed> reportDataWriteSpeed =
                    speed => ReportWriteSpeed(progress, speed, request);
                actualWriteSpeed = BurnData(
                    (object)format,
                    content,
                    request,
                    cancellationToken,
                    reportDataWriteSpeed,
                    writeProgress.ReportDataUpdate);
            }

            stopwatch.Stop();
            result = new OpticalBurnResult(
                request.DeviceId,
                request.MediaKind,
                request.ContentLength,
                stopwatch.Elapsed,
                actualWriteSpeed);
        }
        catch (COMException exception)
        {
            burnFailure = new InvalidOperationException(
                $"Windows 刻录接口在“{currentOperation}”时返回错误 "
                + $"0x{exception.HResult:X8}：{exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            burnFailure = exception;
        }
        finally
        {
            CompleteBurnCleanup(burnFailure, cleanupWarnings,
                () => { if (format is not null) ReleaseComObject((object)format); },
                () => ReleaseComObject((object)recorder));
        }

        progress?.Report(new OpticalBurnProgress(
            "complete",
            "刻录完成，盘片会话已关闭。",
            request.ContentLength,
            request.ContentLength));
        return result!;
    }

    private static OpticalWriteSpeed BurnData(
        dynamic format,
        GeneratedContentComStream content,
        OpticalBurnRequest request,
        CancellationToken cancellationToken,
        Action<OpticalWriteSpeed>? reportWriteSpeed = null,
        Action<object>? reportWriteProgress = null)
    {
        ValidateDataMedia(
            request,
            (int)format.CurrentMediaStatus,
            (long)format.FreeSectorsOnMedia);

        format.BufferUnderrunFreeDisabled = false;
        format.ForceMediaToBeClosed = true;
        format.ForceOverwrite = false;
        if (request.WriteSpeed is not null)
        {
            format.SetWriteSpeed(
                request.WriteSpeed.SectorsPerSecond,
                request.WriteSpeed.RotationTypeIsPureCav);
        }

        OpticalWriteSpeed actualWriteSpeed = new(
            Convert.ToInt32(format.CurrentWriteSpeed),
            Convert.ToBoolean(format.CurrentRotationTypeIsPureCAV));
        actualWriteSpeed.Validate();
        reportWriteSpeed?.Invoke(actualWriteSpeed);
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            TryCancel(format, cdAudio: false));
        // DDiscFormat2DataEvents.Update supplies device-written LBAs, unlike
        // reads from the generated source stream. Preserve the cancellation
        // registration above and keep telemetry limited to the Write lifetime.
        Guid events = new("2735413C-7F64-5B0F-8F00-5D77AFBE261E");
        Action<object, object> update = (_, args) => reportWriteProgress?.Invoke(args);
        bool subscribed = false;
        try
        {
            ComEventsHelper.Combine((object)format, events, 0x200, update);
            subscribed = true;
        }
        catch (COMException) { /* Optional telemetry must not prevent writing. */ }
        try { format.Write(content); }
        finally
        {
            if (subscribed)
            {
                try { ComEventsHelper.Remove((object)format, events, 0x200, update); }
                catch (COMException) { /* Preserve any write error. */ }
            }
        }
        return actualWriteSpeed;
    }

    private static void ValidateRequestedMedia(
        OpticalBurnMediaKind mediaKind,
        dynamic recorder,
        dynamic format)
    {
        object currentProfiles = recorder.CurrentProfiles;
        int[] profiles = currentProfiles is Array values
            ? values.Cast<object>().Select(value => Convert.ToInt32(value)).ToArray()
            : [];
        if (!IsWritableMedia(mediaKind, Convert.ToInt32(format.CurrentPhysicalMediaType), profiles))
        {
            string medium = mediaKind switch
            {
                OpticalBurnMediaKind.CdAudio or OpticalBurnMediaKind.CdData => "CD-R 或 CD-RW",
                OpticalBurnMediaKind.DvdData => "DVD",
                OpticalBurnMediaKind.BluRayData => "未格式化的单层顺序 BD-R（无备用区）",
                _ => throw new ArgumentOutOfRangeException(nameof(mediaKind)),
            };
            throw new InvalidOperationException($"所选刻录模式需要可写的 {medium} 盘片；当前盘片不兼容。");
        }
    }

    private static IReadOnlyList<OpticalWriteSpeed> ReadSupportedWriteSpeeds(dynamic format)
    {
        List<OpticalWriteSpeed> result = [];
        try
        {
            if (format.SupportedWriteSpeedDescriptors is Array descriptors)
            {
                foreach (object? item in descriptors)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        dynamic descriptor = item;
                        int sectorsPerSecond = Convert.ToInt32(descriptor.WriteSpeed);
                        if (sectorsPerSecond > 0)
                        {
                            result.Add(new OpticalWriteSpeed(
                                sectorsPerSecond,
                                Convert.ToBoolean(descriptor.RotationTypeIsPureCAV)));
                        }
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }
            }
        }
        catch (COMException)
        {
            // Older drives may expose only the simple speed list. Fall back below.
        }

        if (result.Count == 0 && format.SupportedWriteSpeeds is Array speeds)
        {
            foreach (object? item in speeds)
            {
                int sectorsPerSecond = Convert.ToInt32(item);
                if (sectorsPerSecond > 0)
                {
                    result.Add(new OpticalWriteSpeed(sectorsPerSecond, false));
                }
            }
        }

        return result
            .Distinct()
            .OrderBy(speed => speed.SectorsPerSecond)
            .ThenBy(speed => speed.RotationTypeIsPureCav)
            .ToArray();
    }

    private static void TryCancel(dynamic format, bool cdAudio)
    {
        try
        {
            if (cdAudio)
            {
                format.CancelAddTrack();
            }
            else
            {
                format.CancelWrite();
            }
        }
        catch
        {
            // The operation may already be finishing; the producer token is also cancelled.
        }
    }

    private static dynamic CreateRecorder(string id)
    {
        dynamic recorder = CreateComObject("IMAPI2.MsftDiscRecorder2");
        recorder.InitializeDiscRecorder(id);
        return recorder;
    }

    private static dynamic CreateComObject(string programmaticId)
    {
        Type type = Type.GetTypeFromProgID(programmaticId, throwOnError: false)
            ?? throw new PlatformNotSupportedException(
                "Windows Image Mastering API 2 (IMAPI2) 不可用。");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"无法创建 Windows 刻录组件 {programmaticId}。");
    }

    private static IReadOnlyList<string> ReadStrings(object? value)
    {
        if (value is not Array array)
        {
            return [];
        }

        List<string> result = [];
        foreach (object? item in array)
        {
            string? text = Convert.ToString(item)?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static void ReleaseComObject(object value)
    {
        if (Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static Task<T> RunOnStaThread<T>(Func<T> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "DiscImageStudio.IMAPI2",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
