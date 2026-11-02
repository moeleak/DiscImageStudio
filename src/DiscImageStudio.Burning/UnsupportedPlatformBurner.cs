namespace DiscImageStudio.Burning;

/// <summary>Placeholder for platforms without a recording backend: lists no devices and
/// explains why burning is unavailable, so the rest of the shell keeps working.</summary>
internal sealed class UnsupportedPlatformBurner : IOpticalDiscBurner
{
    internal const string Message = "当前操作系统暂不支持即时刻录；请导出 ISO / WAV+CUE 后使用系统刻录工具写盘。";

    public Task<IReadOnlyList<OpticalBurnDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OpticalBurnDevice>>([]);
    }

    public Task<IReadOnlyList<OpticalWriteSpeed>> GetSupportedWriteSpeedsAsync(string deviceId,
        OpticalBurnMediaKind mediaKind, CancellationToken cancellationToken = default)
        => Task.FromException<IReadOnlyList<OpticalWriteSpeed>>(new PlatformNotSupportedException(Message));

    public Task<OpticalBurnResult> BurnAsync(OpticalBurnRequest request, IProgress<OpticalBurnProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromException<OpticalBurnResult>(new PlatformNotSupportedException(Message));
}
