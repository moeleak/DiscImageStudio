using DiscImageStudio.Localization;
namespace DiscImageStudio.Hosting;

/// <summary>The UI thread of whichever desktop framework hosts the view models.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> on the UI thread without waiting.</summary>
    void Post(Action action);

    Task InvokeAsync(Action action);

    /// <summary>Creates a stopped UI-thread timer; <paramref name="background"/> requests idle priority.</summary>
    IUiTimer CreateTimer(TimeSpan interval, Action tick, bool background = false);
}

public interface IUiTimer
{
    TimeSpan Interval { get; set; }

    bool IsEnabled { get; }

    void Start();

    void Stop();
}

/// <summary>A file-type filter: <paramref name="Patterns"/> are glob patterns such as <c>*.png</c>.</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns)
{
    public static FileFilter All { get; } = new(L.T("所有文件"), ["*.*"]);

    /// <summary>Parses the legacy WPF filter syntax <c>"Name|*.a;*.b|Name2|*.c"</c>.</summary>
    public static IReadOnlyList<FileFilter> Parse(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        string[] parts = filter.Split('|');
        List<FileFilter> result = [];
        for (int index = 0; index + 1 < parts.Length; index += 2)
        {
            string[] patterns = parts[index + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result.Add(new FileFilter(parts[index].Trim(), patterns));
        }

        return result;
    }

    /// <summary>Formats back into the WPF filter syntax.</summary>
    public static string Format(IEnumerable<FileFilter> filters)
        => string.Join("|", filters.Select(filter => filter.Name + "|" + string.Join(";", filter.Patterns)));
}

/// <summary>Open/save/folder pickers. Every method returns null when the user cancels.</summary>
public interface IFileDialogService
{
    Task<string?> PickOpenAsync(string title, IReadOnlyList<FileFilter> filters);

    Task<string[]?> PickOpenManyAsync(string title, IReadOnlyList<FileFilter> filters);

    Task<string?> PickSaveAsync(IReadOnlyList<FileFilter> filters, string extension, string fileName,
        string? title = null, string? initialDirectory = null);

    Task<string?> PickFolderAsync(string title, string? initialDirectory = null);
}

/// <summary>Opens folders and files with the operating system's default handler.</summary>
public interface IShellLauncher
{
    void OpenFolder(string path);
}

/// <summary>Explorer on Windows, <c>open</c> on macOS, <c>xdg-open</c> elsewhere.</summary>
public sealed class SystemShellLauncher : IShellLauncher
{
    public static SystemShellLauncher Instance { get; } = new();

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        System.Diagnostics.ProcessStartInfo start = OperatingSystem.IsWindows()
            ? new(path) { UseShellExecute = true }
            : OperatingSystem.IsMacOS()
                ? new("open", [path]) { UseShellExecute = false }
                : new("xdg-open", [path]) { UseShellExecute = false };
        using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(start);
    }
}

/// <summary>Icons the view models choose; each host maps them to its own glyphs.
/// Member names match the Lucide icon names.</summary>
public enum AppIcon
{
    SquarePen,
    Disc,
    Disc3,
    DiscAlbum,
}
