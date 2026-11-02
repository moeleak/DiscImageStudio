using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DiscImageStudio.Burning;

/// <summary>
/// Optical drives from sysfs (/sys/class/block/sr*), their mounts from /proc/self/mountinfo,
/// and exclusive SG_IO access to /dev/srN. The file-system roots are injectable so the parsing
/// is testable without a drive.
/// </summary>
internal sealed class LinuxOpticalDeviceProvider(string sysClassBlock = "/sys/class/block",
    string mountInfoPath = "/proc/self/mountinfo", string devDirectory = "/dev") : IMmcDeviceProvider
{
    private const int DefaultTransferBytes = 64 * 1024;
    private const int MaximumTransferBytes = 1024 * 1024;

    public IReadOnlyList<MmcDeviceInfo> EnumerateDevices()
    {
        if (!Directory.Exists(sysClassBlock)) return [];
        Dictionary<string, List<string>> mounts = ReadMounts();
        List<MmcDeviceInfo> devices = [];
        foreach (string directory in Directory.EnumerateFileSystemEntries(sysClassBlock, "sr*")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            if (!IsDriveName(name)) continue;
            string vendor = ReadAttribute(directory, "device/vendor");
            string model = ReadAttribute(directory, "device/model");
            // Device ids are Linux paths: join with "/" whatever the host separator is.
            string devicePath = devDirectory.TrimEnd('/') + "/" + name;
            string label = string.Join(" ", new[] { vendor, model }.Where(text => text.Length > 0));
            string majorMinor = ReadAttribute(directory, "dev");
            IReadOnlyList<string> mountPoints = mounts.TryGetValue(majorMinor, out List<string>? points) ? points : [];
            devices.Add(new MmcDeviceInfo(devicePath,
                (label.Length == 0 ? "光驱" : label) + $" ({devicePath})", mountPoints));
        }

        return devices;
    }

    public IMmcDevice Open(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        MmcDeviceInfo device = EnumerateDevices().FirstOrDefault(candidate => candidate.Id == deviceId)
            ?? throw new InvalidOperationException($"找不到光驱 {deviceId}；请刷新刻录机列表。");
        if (device.MountPoints.Count > 0)
            throw new InvalidOperationException(
                $"{deviceId} 中的盘片已挂载于 {string.Join("、", device.MountPoints)}。请先卸载"
                + $"（例如 umount {device.MountPoints[0]}，或在文件管理器中弹出卷）再刻录。");
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("SG_IO 只在 Linux 上可用。");
        return OpenLinux(deviceId);
    }

    [SupportedOSPlatform("linux")]
    private LinuxMmcDevice OpenLinux(string devicePath)
    {
        int descriptor = LinuxNative.Open(devicePath,
            LinuxNative.ReadWrite | LinuxNative.NonBlocking | LinuxNative.Exclusive | LinuxNative.CloseOnExec);
        if (descriptor < 0) throw DescribeOpenFailure(devicePath, Marshal.GetLastPInvokeError());
        return new LinuxMmcDevice(new LinuxSgIoTransport(devicePath, descriptor, ReadTransferLimit(devicePath)));
    }

    internal static Exception DescribeOpenFailure(string devicePath, int error) => error switch
    {
        LinuxNative.ErrorAccess or LinuxNative.ErrorPermission => new UnauthorizedAccessException(
            $"没有读写 {devicePath} 的权限。请把当前用户加入光驱用户组后重新登录："
            + "Debian / Ubuntu / Fedora 为 cdrom 组（sudo usermod -aG cdrom $USER），Arch 为 optical 组。"),
        LinuxNative.ErrorBusy => new InvalidOperationException(
            $"{devicePath} 正被其他程序占用（或盘片已挂载）。请关闭其他刻录 / 播放软件并卸载盘片后重试。"),
        LinuxNative.ErrorNoEntry or LinuxNative.ErrorNoDevice => new InvalidOperationException(
            $"光驱 {devicePath} 已不可用（可能已断开）；请刷新刻录机列表。"),
        LinuxNative.ErrorNoMedium => new InvalidOperationException("光驱中没有盘片，或托盘未关闭。"),
        _ => new IOException($"无法打开 {devicePath}：{Marshal.GetPInvokeErrorMessage(error)}（errno {error}）。"),
    };

    /// <summary>The block layer's per-command limit for this drive, capped for the writers.</summary>
    internal int ReadTransferLimit(string devicePath)
    {
        string text = ReadAttribute(Path.Combine(sysClassBlock, Path.GetFileName(devicePath)), "queue/max_sectors_kb");
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int kilobytes) && kilobytes > 0
            ? (int)Math.Min((long)kilobytes * 1024, MaximumTransferBytes)
            : DefaultTransferBytes;
    }

    /// <summary>Mount points keyed by "major:minor" (mountinfo field 3; field 5 is the mount
    /// point with \040-style octal escapes).</summary>
    internal Dictionary<string, List<string>> ReadMounts()
    {
        Dictionary<string, List<string>> mounts = new(StringComparer.Ordinal);
        if (!File.Exists(mountInfoPath)) return mounts;
        foreach (string line in File.ReadLines(mountInfoPath))
        {
            string[] fields = line.Split(' ');
            if (fields.Length < 5) continue;
            if (!mounts.TryGetValue(fields[2], out List<string>? points)) mounts[fields[2]] = points = [];
            points.Add(Unescape(fields[4]));
        }

        return mounts;
    }

    internal static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;
        List<byte> bytes = [];
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 3 < value.Length
                && value.AsSpan(index + 1, 3).IndexOfAnyExcept("01234567") < 0)
            {
                bytes.Add((byte)Convert.ToInt32(value.Substring(index + 1, 3), 8));
                index += 3;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[index].ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsDriveName(string name)
        => name.Length > 2 && name.AsSpan(2).IndexOfAnyExcept("0123456789") < 0;

    private static string ReadAttribute(string directory, string relative)
    {
        try
        {
            string path = Path.Combine(directory, relative);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    [SupportedOSPlatform("linux")]
    private sealed class LinuxMmcDevice(LinuxSgIoTransport transport) : IMmcDevice
    {
        public IMmcCommandTransport Transport => transport;

        public void Dispose() => transport.Dispose();
    }
}
