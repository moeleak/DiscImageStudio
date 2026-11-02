namespace DiscImageStudio.Core;

/// <summary>
/// Per-user locations and path-comparison rules that behave consistently on Windows,
/// Linux and macOS. On Unix, <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
/// returns an empty string when the folder does not exist, which would otherwise turn
/// every derived path into one relative to the working directory.
/// </summary>
public static class PlatformPaths
{
    public const string ApplicationFolderName = "DiscImageStudio";

    /// <summary>Case-insensitive on Windows and macOS (NTFS / default APFS), ordinal on Linux.</summary>
    public static StringComparison PathComparison { get; } = OperatingSystem.IsLinux()
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;

    public static StringComparer PathComparer { get; } = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    /// <summary>Per-user application data root: <c>%LOCALAPPDATA%</c>,
    /// <c>~/Library/Application Support</c> or <c>$XDG_DATA_HOME</c> (<c>~/.local/share</c>).</summary>
    public static string LocalApplicationData
    {
        get
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
            {
                return path;
            }

            string home = UserHome;
            return OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support")
                : Path.Combine(home, ".local", "share");
        }
    }

    /// <summary>The application's own per-user data folder (not created here).</summary>
    public static string ApplicationData => Path.Combine(LocalApplicationData, ApplicationFolderName);

    /// <summary>The user's documents folder, falling back to the home directory.</summary>
    public static string Documents
    {
        get
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
            {
                return path;
            }

            string home = UserHome;
            string documents = Path.Combine(home, "Documents");
            return Directory.Exists(documents) ? documents : home;
        }
    }

    public static string UserHome
    {
        get
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify);
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
            {
                return path;
            }

            string? home = Environment.GetEnvironmentVariable("HOME");
            return !string.IsNullOrWhiteSpace(home) ? home : Path.GetTempPath();
        }
    }

    /// <summary>Per-user scratch root for previews and composites. Windows and macOS already
    /// return a per-user temp folder; Linux's shared <c>/tmp</c> is replaced by
    /// <c>$XDG_CACHE_HOME</c> (<c>~/.cache</c>) so users never collide on one folder.</summary>
    public static string UserTemporary
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return Path.GetTempPath();
            }

            string? cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            return !string.IsNullOrWhiteSpace(cache) && Path.IsPathRooted(cache)
                ? cache
                : Path.Combine(UserHome, ".cache");
        }
    }

    public static bool SamePath(string first, string second)
        => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), PathComparison);

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="directory"/>.</summary>
    public static bool IsInside(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, PathComparison);
    }
}
