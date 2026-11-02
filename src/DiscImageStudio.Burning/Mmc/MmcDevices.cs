namespace DiscImageStudio.Burning;

/// <summary>One recorder as an operating-system device: a stable id the backend can reopen,
/// a display name and the places its medium is currently mounted.</summary>
internal sealed record MmcDeviceInfo(string Id, string DisplayName, IReadOnlyList<string> MountPoints);

/// <summary>An opened recorder. Disposing releases exclusive access and the OS handle.</summary>
internal interface IMmcDevice : IDisposable
{
    IMmcCommandTransport Transport { get; }
}

/// <summary>Platform device access beneath the shared MMC burn sequences (Linux SG_IO,
/// macOS SCSITask).</summary>
internal interface IMmcDeviceProvider
{
    IReadOnlyList<MmcDeviceInfo> EnumerateDevices();

    /// <summary>Opens the recorder for exclusive use. Fails with an actionable message when the
    /// medium is mounted, another program holds the drive or the user lacks permission.</summary>
    IMmcDevice Open(string deviceId);
}

/// <summary>
/// Resends a WRITE that the drive rejected without executing it: NOT READY with LONG WRITE IN
/// PROGRESS (02/04/08) or OPERATION IN PROGRESS (02/04/07) while it writes the lead-in or its
/// buffer is full. Every other outcome, including a timeout whose effect is unknown, is
/// returned unchanged, so an accepted range is never written twice.
/// </summary>
internal sealed class MmcBusyRetryTransport(IMmcCommandTransport inner, TimeSpan? maximumWait = null,
    Action<TimeSpan>? sleep = null) : IMmcCommandTransport
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly TimeSpan _maximumWait = maximumWait ?? TimeSpan.FromMinutes(5);
    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;

    public MmcCommandResult Execute(byte[] cdb, int allocationLength, uint timeoutSeconds)
        => inner.Execute(cdb, allocationLength, timeoutSeconds);

    public MmcWriteTransferLimits GetWriteTransferLimits() => inner.GetWriteTransferLimits();

    public MmcCommandResult SendData(byte[] cdb, IntPtr buffer, int bufferLength, uint timeoutSeconds)
    {
        TimeSpan waited = TimeSpan.Zero;
        while (true)
        {
            MmcCommandResult result = inner.SendData(cdb, buffer, bufferLength, timeoutSeconds);
            if (cdb[0] is not (0x2A or 0xAA) || !IsBusy(result) || waited >= _maximumWait) return result;
            _sleep(RetryDelay);
            waited += RetryDelay;
        }
    }

    internal static bool IsBusy(MmcCommandResult result)
    {
        if (result.HResult != MmcCommandResult.CheckConditionStatus) return false;
        (int key, int asc, int ascq) = MmcCommandException.ReadSense(result.Sense);
        return key == 2 && asc == 4 && ascq is 7 or 8;
    }
}
