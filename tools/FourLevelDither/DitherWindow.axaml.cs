using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DiscImageStudio.Imaging;
using DitherProcessor = DiscImageStudio.Imaging.GrayscaleDither;

namespace DiscImageStudio.DitherTool;

public partial class DitherWindow : Window
{
    private Bgra32Image? _source;
    private Gray8Image? _result;
    private string? _inputPath;
    private string? _requestedPath;
    private DitherMode _mode;
    private bool _linearizeSrgb;
    private bool _initialized;
    private CancellationTokenSource? _cancellation;
    private int _revision;
    private bool _closed;
    private bool _saving;
    private bool _closeAfterSave;

    public DitherWindow()
        : this(null)
    {
    }

    public DitherWindow(string? initialImage, DitherMode initialMode = DitherMode.Dvd, bool initialLinearizeSrgb = false)
    {
        _mode = initialMode;
        _linearizeSrgb = initialLinearizeSrgb;
        InitializeComponent();
        ModePicker.SelectedIndex = _mode == DitherMode.Dvd ? 0 : 1;
        GammaToggle.IsChecked = _linearizeSrgb;
        UpdateModeDisplay();
        UpdateGammaDisplay();
        _initialized = true;
        if (initialImage is not null) Opened += async (_, _) => await LoadImageAsync(initialImage);
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);
        Closing += (_, e) =>
        {
            if (!_saving) return;
            e.Cancel = true;
            _closeAfterSave = true;
            StatusText.Text = "正在完成 PNG 保存，保存后将关闭…";
        };
        Closed += (_, _) => { _closed = true; _cancellation?.Cancel(); };
    }

    internal bool HasResult => _result is not null;

    internal Task LoadImageAsync(string path) => ProcessImageAsync(path, null);

    private async Task ProcessImageAsync(string path, Bgra32Image? cachedSource)
    {
        if (_saving || _closed) return;
        _cancellation?.Cancel();
        using CancellationTokenSource cancellation = new();
        _cancellation = cancellation;
        int revision = ++_revision;
        DitherMode mode = _mode;
        bool linearizeSrgb = _linearizeSrgb;
        _requestedPath = path;
        _source = cachedSource;
        _result = null;
        _inputPath = null;
        SourceImage.Source = cachedSource is null ? null : ToBitmap(cachedSource);
        ResultImage.Source = null;
        SourcePlaceholder.IsVisible = cachedSource is null;
        ResultPlaceholder.IsVisible = true;
        SaveButton.IsEnabled = false;
        FileText.Text = Path.GetFileName(path);
        ToolTip.SetTip(FileText, null);
        SourceInfo.Text = string.Empty;
        ToolTip.SetTip(StatusText, null);
        StatusText.Text = $"正在处理 {DitherModes.Label(mode)}抖动（{DitherModes.PaletteLabel(mode)}）· {GammaStatus(linearizeSrgb)}…";
        BusyBar.IsVisible = true;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string fullPath = Path.GetFullPath(path);
            (Bgra32Image source, Gray8Image result) = await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Bgra32Image loaded = cachedSource ?? DitherProcessor.LoadImage(fullPath);
                return (loaded, DitherProcessor.Process(loaded, DitherModes.Levels(mode), cancellation.Token, linearizeSrgb));
            }, cancellation.Token);
            if (_closed || revision != _revision) return;
            _source = source;
            _result = result;
            _inputPath = fullPath;
            SourceImage.Source = ToBitmap(source);
            ResultImage.Source = ToBitmap(result);
            SourcePlaceholder.IsVisible = false;
            ResultPlaceholder.IsVisible = false;
            SourceInfo.Text = $"{source.Width:N0} × {source.Height:N0} 像素";
            FileText.Text = fullPath;
            ToolTip.SetTip(FileText, fullPath);
            SaveButton.IsEnabled = true;
            StatusText.Text = $"{DitherModes.Label(mode)}处理完成 · {GammaStatus(linearizeSrgb)} · {source.Width:N0} × {source.Height:N0} · {stopwatch.Elapsed.TotalSeconds:F2} 秒";
            UpdatePreviewSizes();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!_closed && revision == _revision) StatusText.Text = $"处理失败：{exception.Message}";
        }
        finally
        {
            if (revision == _revision)
            {
                _cancellation = null;
                BusyBar.IsVisible = false;
            }
        }
    }

    private async void Mode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _saving || _closed) return;
        _mode = ModePicker.SelectedIndex == 0 ? DitherMode.Dvd : DitherMode.Cd;
        UpdateModeDisplay();
        if (_requestedPath is not null) await ProcessImageAsync(_requestedPath, _source);
    }

    private void UpdateModeDisplay()
    {
        ResultHeading.Text = DitherModes.Label(_mode) + "抖动结果";
        SaveButton.Content = "保存黑白 PNG";
        ModeHint.Text = _mode == DitherMode.Dvd
            ? "A5 → 黑色 0 · 92 → 白色 255"
            : "0D → 黑色 0 · AA → 白色 255";
    }

    private async void Gamma_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_initialized || _saving || _closed) return;
        _linearizeSrgb = GammaToggle.IsChecked == true;
        UpdateGammaDisplay();
        if (_requestedPath is not null) await ProcessImageAsync(_requestedPath, _source);
    }

    private void UpdateGammaDisplay()
    {
        GammaHint.Text = _linearizeSrgb
            ? "已开启：按线性亮度分配黑白像素，中间调通常更深"
            : "已关闭：保持现有明暗分配";
        ResultDetails.Text = $"蛇形 Floyd–Steinberg · {GammaStatus(_linearizeSrgb)} · 无损 PNG";
        if (_requestedPath is null) StatusText.Text = "就绪 · " + GammaStatus(_linearizeSrgb);
    }

    private static string GammaStatus(bool linearizeSrgb) => linearizeSrgb ? "sRGB 伽马：开" : "sRGB 伽马：关";

    private static readonly FilePickerFileType ImageFiles = new("图片")
    {
        // Linux file choosers match patterns case-sensitively.
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff", "*.gif",
            "*.PNG", "*.JPG", "*.JPEG", "*.BMP", "*.TIF", "*.TIFF", "*.GIF"],
    };

    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择原图",
            AllowMultiple = false,
            FileTypeFilter = [ImageFiles, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await LoadImageAsync(path);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_result is null || _inputPath is null || _saving) return;
        IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_inputPath)!);
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存" + DitherModes.Suffix(_mode) + " PNG",
            FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }],
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_inputPath) + "-" + DitherModes.Suffix(_mode)
                + (_linearizeSrgb ? "-sRGB" : string.Empty) + ".png",
            SuggestedStartLocation = folder,
        });
        if (file?.TryGetLocalPath() is not { } outputPath) return;
        try
        {
            Program.ValidateOutputPath(_inputPath, outputPath);
            Gray8Image result = _result;
            DitherMode mode = _mode;
            bool linearizeSrgb = _linearizeSrgb;
            _saving = true;
            OpenButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            ModePicker.IsEnabled = false;
            GammaToggle.IsEnabled = false;
            BusyBar.IsVisible = true;
            StatusText.Text = "正在保存 PNG · " + GammaStatus(linearizeSrgb) + "…";
            await Task.Run(() => DitherProcessor.SavePng(result, outputPath, DitherModes.Levels(mode)));
            StatusText.Text = $"已保存 · {GammaStatus(linearizeSrgb)}：{outputPath}";
            ToolTip.SetTip(StatusText, outputPath);
        }
        catch (Exception exception)
        {
            _closeAfterSave = false;
            StatusText.Text = $"保存失败：{exception.Message}";
        }
        finally
        {
            _saving = false;
            OpenButton.IsEnabled = true;
            ModePicker.IsEnabled = true;
            GammaToggle.IsEnabled = true;
            SaveButton.IsEnabled = _result is not null;
            BusyBar.IsVisible = false;
            if (_closeAfterSave) Close();
        }
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !_saving && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_saving || e.DataTransfer.TryGetFiles() is not { Length: > 0 } items) return;
        if (items[0].TryGetLocalPath() is { } path) await LoadImageAsync(path);
    }

    private void PreviewMode_Changed(object? sender, RoutedEventArgs e) => UpdatePreviewSizes();

    private void Preview_SizeChanged(object? sender, SizeChangedEventArgs e) => UpdatePreviewSizes();

    private void UpdatePreviewSizes()
    {
        if (_source is null || _result is null) return;
        ResizePreview(SourceImage, SourceScroll, _source.Width, _source.Height);
        ResizePreview(ResultImage, ResultScroll, _result.Width, _result.Height);
    }

    private static Bitmap ToBitmap(Bgra32Image image)
        => CreateBitmap(PixelFormats.Bgra8888, AlphaFormat.Unpremul, image.Pixels, image.Width, image.Height, image.Stride);

    private static Bitmap ToBitmap(Gray8Image image)
        => CreateBitmap(PixelFormats.Gray8, AlphaFormat.Opaque, image.Pixels, image.Width, image.Height, image.Stride);

    private static Bitmap CreateBitmap(PixelFormat format, AlphaFormat alpha, byte[] pixels, int width, int height, int stride)
    {
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(format, alpha, handle.AddrOfPinnedObject(), new PixelSize(width, height), new Vector(96, 96), stride);
        }
        finally
        {
            handle.Free();
        }
    }

    // 100 % means one image pixel per device pixel, as in the WPF tool.
    private void ResizePreview(Image image, ScrollViewer scroll, int pixelWidth, int pixelHeight)
    {
        double renderScaling = RenderScaling > 0 ? RenderScaling : 1;
        double width = pixelWidth / renderScaling;
        double height = pixelHeight / renderScaling;
        double scale = ActualSizeToggle.IsChecked == true ? 1 : Math.Min(1, Math.Min(
            Math.Max(1, scroll.Bounds.Width - 24) / width,
            Math.Max(1, scroll.Bounds.Height - 24) / height));
        image.Width = width * scale;
        image.Height = height * scale;
    }
}
