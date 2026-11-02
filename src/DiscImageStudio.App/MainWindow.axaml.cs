using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiscImageStudio.Burning;
using DiscImageStudio.Controls;
using DiscImageStudio.Core;
using DiscImageStudio.Hosting;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;
using DiscImageStudio.Views;

namespace DiscImageStudio;

/// <summary>The shell window: title row, workspace sidebar, page host, job dock, toasts and
/// the in-window confirmation modal. Pages are created once and swapped in and out.</summary>
public partial class MainWindow : Window
{
    private const double NarrowWindowWidth = 920;

    private readonly ShellViewModel _shell;
    private readonly Control[] _pages;
    private readonly (SideNavItem Item, Workspace Workspace)[] _navItems;
    private readonly PageHeader?[] _pageHeaders = new PageHeader?[6];
    private readonly AppSettingsStore _settingsStore;
    private AppSettings _settings;
    private bool _isSidebarCollapsed;
    private bool _isManuallyCollapsed;
    private bool _closeApproved;
    private bool _isCloseFlowRunning;
    private int _projectUpdateRevision;

    public MainWindow()
        : this(new AppSettings(), new AppSettingsStore())
    {
    }

    internal MainWindow(AppSettings settings, AppSettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        InitializeComponent();
        WindowChromePolicy.Apply(this);
        _shell = new ShellViewModel(AvaloniaUiDispatcher.Instance)
        {
            Dialogs = new AvaloniaFileDialogService(() => this),
        };
        _shell.ConfirmHandler = (title, message, confirmLabel) => BurnConfirmModal.ShowAsync(title, message, confirmLabel);
        _shell.SaveConfirmationHandler = (title, message, confirmLabel, neutralLabel)
            => BurnConfirmModal.ShowThreeWayAsync(title, message, confirmLabel, neutralLabel);
        _shell.ShowProjectDialogHandler = ShowProjectDialogAsync;
        _shell.CalibrationParametersApplied += PlayProjectUpdateCelebration;
        _shell.ProjectSaved += PlayProjectSaveTrail;
        DataContext = _shell;

        // Indexed by the Workspace enum, so navigation, the sidebar and the snapshot CLI share one ordering.
        _pages =
        [
            new HomeView(_shell.Home),
            new DesignView(_shell.Design),
            new LivePreviewView(_shell.LivePreview),
            new ExportView(_shell.Export),
            new BurnView(_shell.Burn),
            new CalibrationView(_shell.Calibration),
        ];
        _navItems =
        [
            (NavDesign, Workspace.Design),
            (NavPreview, Workspace.Preview),
            (NavExport, Workspace.Export),
            (NavBurn, Workspace.Burn),
            (NavCalibration, Workspace.Calibration),
        ];
        foreach ((SideNavItem item, Workspace workspace) in _navItems)
        {
            item.Activated += (_, _) => _shell.NavigateTo(workspace);
        }

        _shell.PropertyChanged += Shell_PropertyChanged;
        UpdateNavActive(Workspace.Home);
        UpdateProjectHeader();
        UpdateStageBars();
        PageHost.Content = _pages[(int)Workspace.Home];
        SidebarBorder.Transitions =
        [
            new DoubleTransition { Property = WidthProperty, Duration = Motion.Fast, Easing = Motion.EaseOut },
        ];
        SizeChanged += (_, _) => UpdateSidebarCollapse();
        AddHandler(KeyDownEvent, MainWindow_PreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    internal ShellViewModel Shell => _shell;

    internal bool HasUnsavedWork => _shell.IsProjectOpen && _shell.IsDirty;

    internal bool IsLivePreviewReady => _shell.LivePreview.IsLivePreviewReady;

    internal bool IsTonePreviewReady
        => !_shell.IsProjectOpen || !_shell.Design.HasLayers || _shell.Design.Tone.IsTonePreviewReady;

    /// <summary>Called by the application lifetime when the session ends (logoff/shutdown):
    /// unsaved changes get the same save/discard/cancel prompt as closing the window.</summary>
    internal async Task<bool> ConfirmSessionEndAsync()
    {
        if (!_shell.IsProjectOpen || !_shell.IsDirty || _isCloseFlowRunning) return true;
        CancelEventArgs decision = new();
        try { await _shell.HandleWindowClosingAsync(decision); }
        catch (InvalidOperationException) { return false; }
        _closeApproved = !decision.Cancel;
        return _closeApproved;
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_Opened;
        UpdateStageBars();
        _shell.MarkWindowLoaded();
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || !_shell.IsProjectOpen || !_shell.IsDirty) return;
        e.Cancel = true;
        if (_isCloseFlowRunning) return;
        _isCloseFlowRunning = true;
        try
        {
            CancelEventArgs decision = new();
            try { await _shell.HandleWindowClosingAsync(decision); }
            catch (InvalidOperationException) { return; }
            if (decision.Cancel) return;
        }
        finally
        {
            _isCloseFlowRunning = false;
        }

        _closeApproved = true;
        Close();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _shell.LivePreview.OnWindowClosed();
        _shell.Calibration.OnWindowClosed();
    }

    /// <summary>Command key: Ctrl on Windows/Linux, ⌘ on macOS.</summary>
    private static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (BurnConfirmModal.IsOpen)
        {
            if (BurnConfirmModal.HandleKeyDown(e.Key)) e.Handled = true;
            return;
        }

        if (e.Key == Key.OemComma && e.KeyModifiers == CommandModifier)
        {
            e.Handled = true;
            _ = OpenSettingsDialogAsync();
            return;
        }

        if (e.Key == Key.S && (e.KeyModifiers == CommandModifier || e.KeyModifiers == (CommandModifier | KeyModifiers.Shift))
            && _shell.IsProjectOpen)
        {
            e.Handled = true;
            _ = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? _shell.SaveProjectAsCommand.ExecuteAsync(null)
                : _shell.SaveProjectCommand.ExecuteAsync(null);
        }
    }

    private void EditProject_Click(object? sender, RoutedEventArgs e) => _ = _shell.EditProjectCommand.ExecuteAsync(null);

    private void PlayProjectSaveTrail()
    {
        if (!_shell.IsProjectOpen || !SidebarBorder.IsVisible || !ProjectHeaderButton.IsVisible) return;
        ProjectSaveTrail.Play();
    }

    /// <summary>Applied-calibration acknowledgement: a light band sweeps the project header
    /// once while a "parameters updated" tag fades in and out.</summary>
    private void PlayProjectUpdateCelebration()
    {
        if (!_shell.IsProjectOpen || !SidebarBorder.IsVisible || !ProjectHeaderButton.IsVisible)
        {
            _shell.ShowToast("标定参数已应用", "本工程的参数已更新。", ToastKind.Success);
            return;
        }

        int revision = ++_projectUpdateRevision;
        double width = ProjectHeaderHost.Bounds.Width > 0 ? ProjectHeaderHost.Bounds.Width : 180;
        double band = Math.Max(72, width * 0.55);
        ProjectUpdateShineBand.Width = band;
        ProjectUpdateShine.Opacity = 1;
        Animation sweep = new()
        {
            Duration = TimeSpan.FromMilliseconds(700),
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                ShineFrame(0, -band * 1.5),
                ShineFrame(1, width + band * 1.5),
            },
        };
        _ = RunShineAsync(sweep, revision);
        Animation badge = new()
        {
            Duration = TimeSpan.FromSeconds(2.6),
            Children =
            {
                OpacityFrame(0, 0),
                OpacityFrame(180 / 2600.0, 1),
                OpacityFrame(2.0 / 2.6, 1),
                OpacityFrame(1, 0),
            },
        };
        _ = badge.RunAsync(ProjectUpdateBadge);
    }

    private async Task RunShineAsync(Animation sweep, int revision)
    {
        await sweep.RunAsync(ProjectUpdateShineBand);
        if (revision != _projectUpdateRevision) return;
        await new Animation
        {
            Duration = Motion.Base,
            Easing = Motion.EaseOut,
            Children = { OpacityFrame(0, 1), OpacityFrame(1, 0) },
        }.RunAsync(ProjectUpdateShine);
        if (revision == _projectUpdateRevision) ProjectUpdateShine.Opacity = 0;
    }

    private static KeyFrame ShineFrame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(RenderTransformProperty,
                TransformOperations.Parse(FormattableString.Invariant($"translateX({x}px) skewX(-18deg)"))),
        },
    };

    private static KeyFrame OpacityFrame(double cue, double opacity) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(OpacityProperty, opacity) },
    };

    private void TitleBar_AppMenuRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is not TitleBar titleBar) return;
        MenuItem settings = new() { Header = "设置…", InputGesture = new KeyGesture(Key.OemComma, CommandModifier) };
        settings.Click += (_, _) => _ = OpenSettingsDialogAsync();
        MenuItem about = new() { Header = "关于…" };
        about.Click += (_, _) => _ = new AboutWindow(_shell.About).ShowDialog(this);
        ContextMenu menu = new()
        {
            ItemsSource = new[] { settings, about },
            Placement = PlacementMode.BottomEdgeAlignedRight,
        };
        menu.Open(titleBar.AppMenuAnchor);
    }

    private async Task OpenSettingsDialogAsync()
    {
        SettingsViewModel viewModel = new(_settings, _settingsStore) { IsBusyProvider = () => _shell.IsBusy };
        SettingsWindow window = new(viewModel);
        PropertyChangedEventHandler busyHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsBusy)) viewModel.RefreshBusyState();
        };
        _shell.PropertyChanged += busyHandler;
        viewModel.RefreshBusyState();
        try
        {
            if (await window.ShowDialog<bool?>(this) == true && viewModel.SavedSettings is { } saved)
            {
                ApplySettings(saved);
            }
        }
        finally
        {
            _shell.PropertyChanged -= busyHandler;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        App.ApplyTheme(settings.Theme);
        string cacheDirectory = TempFiles.TryConfigureCacheDirectory(settings.CacheDirectory);
        if (settings.CacheDirectory.Length > 0
            && !string.Equals(cacheDirectory, settings.CacheDirectory, PlatformPaths.PathComparison))
        {
            _shell.ShowToast("缓存位置不可用", "已改用系统临时目录。", ToastKind.Warning);
        }
    }

    private void UpdateProjectHeader()
    {
        SidebarBorder.IsVisible = _shell.IsProjectMode;
        DockHost.IsVisible = _shell.IsProjectMode;
        if (!_shell.IsProjectOpen) return;
        ProjectNameText.Text = _shell.CurrentProject!.Name;
        ProjectFamilyText.Text = _shell.CurrentProject.FamilyLabel;
        ProjectDirtyText.IsVisible = _shell.IsDirty;
    }

    private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.CurrentWorkspace) or nameof(ShellViewModel.IsProjectOpen)
            or nameof(ShellViewModel.IsDirty) or nameof(ShellViewModel.CurrentProject))
        {
            UpdateNavActive(_shell.CurrentWorkspace);
            ShowPage(_shell.CurrentWorkspace);
            UpdateProjectHeader();
            if (e.PropertyName is nameof(ShellViewModel.CurrentWorkspace)) UpdateStageBars();
        }
    }

    private void UpdateStageBars()
    {
        for (int index = 0; index < _pages.Length; index++)
        {
            if (_pageHeaders[index] is null)
            {
                if (_pages[index].GetLogicalDescendants().OfType<PageHeader>().FirstOrDefault() is not { } header) continue;
                header.StageActivated += workspace => _shell.NavigateTo(workspace);
                _pageHeaders[index] = header;
            }

            _pageHeaders[index]!.Current = _shell.CurrentWorkspace;
        }
    }

    private async Task<ProjectDialogResult?> ShowProjectDialogAsync(ProjectDialogRequest request)
    {
        ProjectDialogViewModel viewModel = new(_shell.State, request, new RecentTemplateStore(), _settings.DefaultProjectDirectory);
        if (request.EditMode && _shell.CurrentProject is { } project)
        {
            viewModel.Family = project.Family;
            viewModel.Name = project.Name;
            if (project.Family == OpticalDiscFamily.Dvd)
            {
                viewModel.InnerRadiusText = _shell.State.DvdInnerRadius;
                viewModel.OuterRadiusText = _shell.State.DvdOuterRadius;
                viewModel.SectorsText = _shell.State.DvdTotalSectors;
                viewModel.PitchLinearText = _shell.State.DvdPitchLinear;
                viewModel.PitchQuadraticText = _shell.State.DvdPitchQuadratic;
                viewModel.PitchCubicText = _shell.State.DvdPitchCubic;
            }
            else if (project.Family == OpticalDiscFamily.BluRay)
            {
                viewModel.InnerRadiusText = _shell.State.BdInnerRadius;
                viewModel.OuterRadiusText = _shell.State.BdOuterRadius;
                viewModel.SectorsText = _shell.State.BdTotalSectors;
            }
            else
            {
                viewModel.InnerRadiusText = _shell.State.CdInnerRadius;
                viewModel.OuterRadiusText = _shell.State.CdOuterRadius;
                viewModel.SectorsText = _shell.State.CdSectors;
            }
        }

        ProjectDialogWindow window = new(viewModel);
        return await window.ShowDialog<bool?>(this) == true ? viewModel.Result : null;
    }

    private void UpdateNavActive(Workspace workspace)
    {
        foreach ((SideNavItem item, Workspace itemWorkspace) in _navItems) item.IsActive = itemWorkspace == workspace;
    }

    private void ShowPage(Workspace workspace)
    {
        Control page = _pages[(int)workspace];
        if (ReferenceEquals(PageHost.Content, page)) return;
        PageHost.Content = page;
        foreach (ScrollViewer viewer in page.GetLogicalDescendants().OfType<ScrollViewer>()) viewer.Offset = default;
        PlayEntrance(page);
    }

    private static void PlayEntrance(Control page)
    {
        _ = new Animation
        {
            Duration = Motion.Base,
            Easing = Motion.EaseOut,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(RenderTransformProperty, TransformOperations.Parse("translateY(8px)")) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(RenderTransformProperty, TransformOperations.Parse("translateY(0px)")) } },
            },
        }.RunAsync(page);
    }

    private void SidebarCollapseToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (Bounds.Width < NarrowWindowWidth) return;
        _isManuallyCollapsed = !_isManuallyCollapsed;
        UpdateSidebarCollapse();
    }

    private void UpdateSidebarCollapse()
    {
        bool narrow = Bounds.Width < NarrowWindowWidth;
        bool collapsed = narrow || _isManuallyCollapsed;
        SidebarCollapseToggle.IsVisible = !narrow;
        if (collapsed == _isSidebarCollapsed) return;
        _isSidebarCollapsed = collapsed;
        SidebarBorder.Width = collapsed
            ? ThemeValues.Double("SidebarCollapsedWidth", 60)
            : ThemeValues.Double("SidebarWidth", 264);
        foreach ((SideNavItem item, _) in _navItems) item.IsCollapsed = collapsed;
        ProjectHeaderButton.IsVisible = !collapsed;
        TopNavPanel.Margin = collapsed ? new Thickness(0, 20, 0, 0) : new Thickness(0, 16, 0, 0);
        SidebarGrid.Margin = collapsed ? default : new Thickness(24, 0, 24, 0);
        BackHomeButton.HorizontalAlignment = collapsed ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
        CollapseLeftIcon.IsVisible = !collapsed;
        CollapseRightIcon.IsVisible = collapsed;
        ToolTip.SetTip(SidebarCollapseToggle, collapsed ? "展开侧边栏" : "收起侧边栏");
    }

    // ---- Snapshot hooks (ui-snapshot CLI) ----

    internal void ConfigureSnapshot(Workspace workspace, string? previewPath, string? liveInputPath = null,
        string? liveDisc = null, string? liveMode = null, string? projectPath = null, bool expandJobDock = false,
        string? burnSource = null)
    {
        if (!string.IsNullOrWhiteSpace(projectPath)) _shell.OpenProjectForSnapshot(Path.GetFullPath(projectPath));
        _shell.SelectWorkspace(workspace);
        if (workspace == Workspace.Burn)
        {
            if (string.Equals(burnSource, "calibration", StringComparison.OrdinalIgnoreCase))
            {
                _shell.Calibration.EnsureTarget();
                _shell.Burn.UseCalibrationPattern = true;
            }

            _shell.Burn.UpdateSourceSummary();
        }

        if (workspace == Workspace.Calibration) _shell.Calibration.EnsureTarget();
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            string fullPath = Path.GetFullPath(previewPath);
            _shell.SelectWorkspace(Workspace.Preview);
            _shell.LivePreview.PrepareResultPreview(fullPath);
            _shell.LivePreview.ShowResultPreview(fullPath);
        }

        if (!string.IsNullOrWhiteSpace(liveInputPath))
        {
            OpticalDiscFamily family = liveDisc?.ToLowerInvariant() switch
            {
                "dvd" => OpticalDiscFamily.Dvd,
                "bd" or "bluray" or "blu-ray" => OpticalDiscFamily.BluRay,
                _ => OpticalDiscFamily.CompactDisc,
            };
            bool calibration = liveMode is null || string.Equals(liveMode, "calibrate", StringComparison.OrdinalIgnoreCase);
            _shell.LivePreview.SetDiscType(family, clearResult: false);
            _shell.LivePreview.SetPreviewMode(calibration, clearResult: false);
            string fullInput = Path.GetFullPath(liveInputPath);
            switch (family, calibration)
            {
                case (OpticalDiscFamily.Dvd, true): _shell.State.DvdImagePath = fullInput; break;
                case (OpticalDiscFamily.BluRay, true): _shell.State.BdImagePath = fullInput; break;
                case (_, true): _shell.State.CdImagePath = fullInput; break;
                case (OpticalDiscFamily.Dvd, false): _shell.State.DvdIsoPath = fullInput; break;
                case (OpticalDiscFamily.BluRay, false): _shell.State.BdIsoPath = fullInput; break;
                default: _shell.State.CdTrackPath = fullInput; break;
            }

            _shell.SelectWorkspace(Workspace.Preview);
        }

        if (expandJobDock) _shell.IsJobDockExpanded = true;
    }

    internal void ShowBurnProgressForSnapshot(string phase)
    {
        if (phase is not ("write" or "buffer" or "finalize"))
            throw new ArgumentException("Burn progress snapshot must be write, buffer or finalize.");
        OpticalBurnMediaKind kind = _shell.Burn.IsBluRayBurn ? OpticalBurnMediaKind.BluRayData
            : _shell.Burn.IsDvdBurn ? OpticalBurnMediaKind.DvdData : OpticalBurnMediaKind.CdAudio;
        long total = kind == OpticalBurnMediaKind.CdAudio ? 800L * 1024 * 1024
            : kind == OpticalBurnMediaKind.BluRayData ? 12219392L * 2048 : 2295104L * 2048;
        _shell.SetBusy(true);
        _shell.BeginBurnDockJob(kind, total);
        _shell.ReportBurnProgress(new(OpticalBurnStages.Buffer, "正在准备防断流缓冲区…", 0, total));
        if (phase != "buffer")
            _shell.ReportBurnProgress(new(OpticalBurnStages.Write, "刻录机开始写入。", total / 3, total, 8.25 * 1024 * 1024));
        if (phase == "finalize")
            _shell.ReportBurnProgress(new(OpticalBurnStages.Finalize, "正在刷新缓存并关闭刻录会话…", total / 3, total));
    }

    internal void PlayProjectUpdateForSnapshot() => PlayProjectUpdateCelebration();

    internal void PlayProjectSaveForSnapshot()
    {
        if (_shell.IsProjectOpen && SidebarBorder.IsVisible && ProjectHeaderButton.IsVisible) ProjectSaveTrail.ShowAt(0.62);
    }

    internal async Task LoadCalibrationSnapshotAsync(string path)
    {
        _shell.SelectWorkspace(Workspace.Calibration);
        await _shell.Calibration.LoadSessionFromAsync(path);
        if (_shell.Calibration.SolveCommand.CanExecute(null)) await _shell.Calibration.SolveCommand.ExecuteAsync(null);
    }

    internal void ConfigureCalibrationSnapshotView(string mode)
    {
        if (mode is not ("pattern" or "closeup" or "target"))
            throw new ArgumentException("Calibration snapshot view must be pattern, closeup or target.");
        _shell.SelectWorkspace(Workspace.Calibration);
        if (mode == "target")
        {
            // Step one auto-collapses once a target exists; snapshots need it open again to
            // capture the target-generation card.
            _shell.Calibration.StepOneExpanded = true;
            UpdateLayout();
            return;
        }

        // The burn-pattern overlay is view-model state; no reach-in into the view.
        _shell.Calibration.ShowTargetPreview = mode == "pattern";
        UpdateLayout();
        CalibrationPhotoCanvas canvas = CalibrationPage.PhotoCanvas;
        canvas.ResetView();
        if (mode != "closeup") return;
        var points = _shell.Calibration.TracePoints;
        if (points.Count < 2) throw new InvalidOperationException("Closeup needs a loaded calibration trace.");
        Point minimum = new(points.Min(p => p.XMm), points.Min(p => p.YMm));
        Point maximum = new(points.Max(p => p.XMm), points.Max(p => p.YMm));
        Point a = canvas.WorldToView(minimum), b = canvas.WorldToView(maximum);
        double zoom = Math.Min((canvas.Bounds.Width - 90) / Math.Max(1, b.X - a.X),
            (canvas.Bounds.Height - 90) / Math.Max(1, b.Y - a.Y));
        canvas.ZoomBy(Math.Clamp(zoom, 1, 8));
        Point center = canvas.WorldToView(new Point((minimum.X + maximum.X) / 2, (minimum.Y + maximum.Y) / 2));
        canvas.PanBy(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2) - center);
    }

    internal void ExpandDisclosureForSnapshot()
    {
        foreach (Expander expander in _pages[(int)_shell.CurrentWorkspace].GetLogicalDescendants().OfType<Expander>())
        {
            if (expander is not StepCard) expander.IsExpanded = true;
        }
    }

    internal void ScrollWorkspace(double offset)
    {
        if (offset <= 0) return;
        if ((PageHost.Content as Control)?.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } viewer)
            viewer.Offset = new Vector(0, offset);
    }

    internal void ShowHomeHoverForSnapshot()
    {
        if (PageHost.GetVisualDescendants().OfType<RecentProjectCard>().FirstOrDefault() is { } card)
            card.Classes.Add("snapshot-hover");
    }

    internal CalibrationView CalibrationPage => (CalibrationView)_pages[(int)Workspace.Calibration];
}
