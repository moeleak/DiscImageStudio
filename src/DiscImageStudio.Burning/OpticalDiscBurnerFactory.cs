namespace DiscImageStudio.Burning;

/// <summary>Chooses the recording backend for the running operating system.</summary>
public static class OpticalDiscBurnerFactory
{
    public static IOpticalDiscBurner CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsImapiBurner();
        }

        if (OperatingSystem.IsLinux())
        {
            return new MmcOpticalDiscBurner(new LinuxOpticalDeviceProvider());
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MmcOpticalDiscBurner(new MacOpticalDeviceProvider());
        }

        return new UnsupportedPlatformBurner();
    }

    /// <summary>True for the MMC backends (Linux, macOS) until they are verified on real
    /// recorders; the shell labels burning there as experimental.</summary>
    public static bool IsCurrentPlatformBackendExperimental => !OperatingSystem.IsWindows();

    /// <summary>Short backend name for diagnostics and the CLI.</summary>
    public static string DescribeCurrentPlatformBackend()
        => OperatingSystem.IsWindows() ? "Windows IMAPI2"
            : OperatingSystem.IsLinux() ? "Linux SG_IO (MMC)"
            : OperatingSystem.IsMacOS() ? "macOS IOKit SCSITask (MMC)"
            : "unsupported";
}
