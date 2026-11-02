using DiscImageStudio.Localization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DiscImageStudio.Imaging;

namespace DiscImageStudio.Hosting;

/// <summary>Converts the view models' portable rasters into Avalonia bitmaps, once per raster.</summary>
public static class AvaloniaBitmaps
{
    private static readonly ConditionalWeakTable<IRasterImage, Bitmap> Cache = new();

    public static Bitmap? ToBitmap(IRasterImage? image)
        => image is null ? null : Cache.GetValue(image, static raster => Create(raster));

    private static Bitmap Create(IRasterImage raster)
    {
        (PixelFormat format, AlphaFormat alpha, byte[] pixels, int stride) = raster switch
        {
            Bgra32Image bgra => (PixelFormats.Bgra8888, AlphaFormat.Unpremul, bgra.Pixels, bgra.Stride),
            Gray8Image gray => (PixelFormats.Gray8, AlphaFormat.Opaque, gray.Pixels, gray.Stride),
            _ => throw new NotSupportedException($"Unsupported raster type {raster.GetType().Name}."),
        };
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            // Always 96 DPI on screen: one image pixel per DIP, as the canvases assume when they
            // pass pixel rectangles to DrawImage (a 72 DPI JPEG would otherwise draw at 75 %).
            // The file's own resolution stays on the raster for PNG output.
            return new Bitmap(format, alpha, handle.AddrOfPinnedObject(), new PixelSize(raster.Width, raster.Height),
                new Vector(96, 96), stride);
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>The Avalonia UI thread behind the view models' dispatcher abstraction.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; } = new();

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public IUiTimer CreateTimer(TimeSpan interval, Action tick, bool background = false)
        => new AvaloniaUiTimer(interval, tick, background ? DispatcherPriority.Background : DispatcherPriority.Normal);

    private sealed class AvaloniaUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaUiTimer(TimeSpan interval, Action tick, DispatcherPriority priority)
        {
            _timer = new DispatcherTimer(priority) { Interval = interval };
            _timer.Tick += (_, _) => tick();
        }

        public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }

        public bool IsEnabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}

/// <summary>Native open/save/folder pickers through the window's StorageProvider (portal
/// dialogs on Linux, NSOpenPanel on macOS, the common item dialog on Windows).</summary>
public sealed class AvaloniaFileDialogService(Func<TopLevel?> topLevelProvider) : IFileDialogService
{
    public async Task<string?> PickOpenAsync(string title, IReadOnlyList<FileFilter> filters)
        => (await OpenAsync(title, filters, allowMultiple: false))?.FirstOrDefault();

    public Task<string[]?> PickOpenManyAsync(string title, IReadOnlyList<FileFilter> filters)
        => OpenAsync(title, filters, allowMultiple: true);

    public async Task<string?> PickSaveAsync(IReadOnlyList<FileFilter> filters, string extension, string fileName,
        string? title = null, string? initialDirectory = null)
    {
        if (topLevelProvider()?.StorageProvider is not { CanSave: true } storage) return null;
        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title ?? L.T("选择输出位置"),
            SuggestedFileName = fileName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = ToFileTypes(filters),
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(storage, initialDirectory),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory = null)
    {
        if (topLevelProvider()?.StorageProvider is not { CanPickFolder: true } storage) return null;
        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(storage, initialDirectory),
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<string[]?> OpenAsync(string title, IReadOnlyList<FileFilter> filters, bool allowMultiple)
    {
        if (topLevelProvider()?.StorageProvider is not { CanOpen: true } storage) return null;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = ToFileTypes(filters),
        });
        string[] paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        return paths.Length == 0 ? null : paths;
    }

    private static async Task<IStorageFolder?> FolderAsync(IStorageProvider storage, string? path)
        => string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : await storage.TryGetFolderFromPathAsync(path);

    /// <summary>Linux file choosers match patterns case-sensitively, so upper-case variants
    /// of every extension pattern are added (".PNG" from a camera must still be listed).</summary>
    private static List<FilePickerFileType> ToFileTypes(IReadOnlyList<FileFilter> filters)
        => filters.Select(filter => new FilePickerFileType(filter.Name)
        {
            Patterns = filter.Patterns
                .SelectMany(pattern => pattern == "*.*" ? [pattern] : new[] { pattern, pattern.ToUpperInvariant() })
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        }).ToList();
}
