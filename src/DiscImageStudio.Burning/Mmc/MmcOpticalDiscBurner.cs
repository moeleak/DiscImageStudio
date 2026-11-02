using System.Diagnostics;
using static DiscImageStudio.Burning.OpticalBurnWorkflows;

namespace DiscImageStudio.Burning;

/// <summary>
/// Linux and macOS backend: the shared MMC burn sequences over a platform device provider.
/// BD-R uses the same sequential SRM session as Windows; DVD-R/+R are written disc-at-once or
/// sequentially and finalized; CDs are written session-at-once with a cue sheet.
/// </summary>
internal sealed class MmcOpticalDiscBurner(IMmcDeviceProvider provider) : IOpticalDiscBurner
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(1);
    private readonly OpticalBurnOperationGate _operationGate = new();

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
        => _operationGate.RunAsync(() => Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (IReadOnlyList<OpticalBurnDevice>)provider.EnumerateDevices()
                .Select(device => new OpticalBurnDevice(device.Id, device.DisplayName, device.MountPoints))
                .ToArray();
        }), cancellationToken);

    public Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(string deviceId,
        OpticalBurnMediaKind mediaKind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        _ = OpticalBurnMedia.GetSectorBytes(mediaKind);
        return _operationGate.RunAsync(() => Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IMmcDevice device = provider.Open(deviceId);
            MmcCommandSet commands = new(device.Transport);
            commands.WaitUntilReady(ReadyTimeout);
            MmcMediaPolicy.Require(mediaKind, commands.ReadCurrentProfile());
            return MmcSpeedControl.ToSpeeds(commands.GetWriteSpeeds(), mediaKind);
        }), cancellationToken);
    }

    public Task<OpticalBurnResult> BurnAsync(OpticalBurnRequest request, IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return _operationGate.RunAsync(() => Run(() => Burn(request, progress, cancellationToken)), cancellationToken);
    }

    private OpticalBurnResult Burn(OpticalBurnRequest request, IProgress<OpticalBurnProgress>? progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ImapiWriteProgressReporter writeProgress = new(request.MediaKind, request.ContentLength,
            value => progress?.Report(value));
        List<string> cleanupWarnings = [];
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Device, "正在检查刻录机和空白介质…", 0,
            request.ContentLength));
        IMmcDevice device = provider.Open(request.DeviceId);
        OpticalBurnResult? result = null;
        Exception? burnFailure = null;
        try
        {
            MmcBusyRetryTransport transport = new(device.Transport);
            MmcCommandSet commands = new(transport);
            commands.WaitUntilReady(ReadyTimeout);
            int profile = commands.ReadCurrentProfile();
            MmcMediaPolicy.Require(request.MediaKind, profile);
            if (request.WriteSpeed is null)
            {
                request = request with
                {
                    WriteSpeed = ResolveWriteSpeed(null, MmcSpeedControl.ToSpeeds(commands.GetWriteSpeeds(), request.MediaKind)),
                };
                progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Speed,
                    request.WriteSpeed is null
                        ? "当前盘片未报告可选速度，将使用光驱的保守回退设置…"
                        : $"自动选择最低写入速度：{request.WriteSpeed.GetMultiplier(request.MediaKind):0.#}×"
                            + $"（{request.WriteSpeed.GetMegabytesPerSecond(request.MediaKind):0.0} MB/s）…",
                    0, request.ContentLength));
            }

            using GeneratedContentComStream content = new(request.ContentLength, request.ProduceContent,
                (completed, total) => progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Transfer,
                    $"正在向刻录机传输 {completed / (1024.0 * 1024.0):F1} / {total / (1024.0 * 1024.0):F1} MiB…",
                    completed, total)),
                cancellationToken, request.EffectiveBufferOptions);
            progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Buffer, "正在准备防断流缓冲区…", 0,
                request.ContentLength));
            content.WaitUntilPrebuffered(cancellationToken);
            // Preparation may take minutes. Make sure the same kind of medium is still loaded.
            commands.WaitUntilReady(ReadyTimeout);
            if (commands.ReadCurrentProfile() != profile)
                throw new InvalidOperationException("盘片在准备期间被更换，已取消刻录。");

            Action<string> reportOperation = operation => writeProgress.ReportStage(
                operation.Contains("封盘", StringComparison.Ordinal) || operation.Contains("导出区", StringComparison.Ordinal)
                    ? OpticalBurnStages.Finalize : OpticalBurnStages.Media, operation);
            Action<OpticalWriteSpeed> reportSpeed = speed => ReportWriteSpeed(progress, speed, request);
            OpticalWriteSpeed actualWriteSpeed;
            switch (request.MediaKind)
            {
                case OpticalBurnMediaKind.BluRayData:
                {
                    using MmcBluRaySequentialSession session = new(transport, writeProgress.ReportAcceptedBytes,
                        cleanupWarnings.Add,
                        message => progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Diagnostic, message, 0,
                            request.ContentLength)));
                    actualWriteSpeed = BurnBluRay(session, content, request, cancellationToken, reportOperation,
                        reportSpeed, cleanupWarnings.Add);
                    break;
                }
                case OpticalBurnMediaKind.DvdData:
                    actualWriteSpeed = MmcDvdBurn.Burn(commands, profile, content, request, cancellationToken,
                        reportOperation, reportSpeed, writeProgress.ReportAcceptedBytes, cleanupWarnings.Add);
                    break;
                default:
                    actualWriteSpeed = MmcCdSaoBurn.Burn(commands, content, request, cancellationToken,
                        reportOperation, reportSpeed, writeProgress.ReportAcceptedBytes, cleanupWarnings.Add);
                    break;
            }

            stopwatch.Stop();
            result = new OpticalBurnResult(request.DeviceId, request.MediaKind, request.ContentLength,
                stopwatch.Elapsed, actualWriteSpeed);
        }
        catch (Exception exception)
        {
            burnFailure = cancellationToken.IsCancellationRequested && exception is not OperationCanceledException
                ? new OperationCanceledException("刻录已取消。", exception, cancellationToken)
                : exception;
        }
        finally
        {
            CompleteBurnCleanup(burnFailure, cleanupWarnings, [(device.Dispose, "释放刻录机")]);
        }

        progress?.Report(new OpticalBurnProgress(OpticalBurnStages.Complete, "刻录完成，盘片会话已关闭。",
            request.ContentLength, request.ContentLength));
        return result!;
    }

    // Device commands block for the whole burn; keep them off the thread pool.
    private static Task<T> Run<T>(Func<T> operation)
        => Task.Factory.StartNew(operation, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
