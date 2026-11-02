using DiscImageStudio.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.Hosting;

namespace DiscImageStudio.Controls;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string> ProjectTitleProperty =
        AvaloniaProperty.Register<TitleBar, string>(nameof(ProjectTitle), string.Empty);
    public static readonly StyledProperty<bool> ShowMinimizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinimizeButton), true);
    public static readonly StyledProperty<bool> ShowMaximizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximizeButton), true);
    public static readonly StyledProperty<bool> ShowAppMenuProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowAppMenu));
    public static readonly StyledProperty<bool> ShowSettingsButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowSettingsButton));

    public static readonly RoutedEvent<RoutedEventArgs> SettingsRequestedEvent =
        RoutedEvent.Register<TitleBar, RoutedEventArgs>(nameof(SettingsRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> AppMenuRequestedEvent =
        RoutedEvent.Register<TitleBar, RoutedEventArgs>(nameof(AppMenuRequested), RoutingStrategies.Bubble);

    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Bar.PointerPressed += Bar_PointerPressed;
        Bar.DoubleTapped += (_, _) =>
        {
            if (WindowChromePolicy.DrawsCaptionButtons && ShowMaximizeButton) ToggleMaximize();
        };
        AttachedToVisualTree += (_, _) => AttachWindow();
        DetachedFromVisualTree += (_, _) => DetachWindow();
        ApplyWindowButtonVisibility();
    }

    public string ProjectTitle { get => GetValue(ProjectTitleProperty); set => SetValue(ProjectTitleProperty, value); }
    public bool ShowMinimizeButton { get => GetValue(ShowMinimizeButtonProperty); set => SetValue(ShowMinimizeButtonProperty, value); }
    public bool ShowMaximizeButton { get => GetValue(ShowMaximizeButtonProperty); set => SetValue(ShowMaximizeButtonProperty, value); }
    public bool ShowAppMenu { get => GetValue(ShowAppMenuProperty); set => SetValue(ShowAppMenuProperty, value); }

    /// <summary>Shows a direct, labeled settings action. Only the main window opts in.</summary>
    public bool ShowSettingsButton { get => GetValue(ShowSettingsButtonProperty); set => SetValue(ShowSettingsButtonProperty, value); }

    public event EventHandler<RoutedEventArgs>? SettingsRequested
    {
        add => AddHandler(SettingsRequestedEvent, value);
        remove => RemoveHandler(SettingsRequestedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? AppMenuRequested
    {
        add => AddHandler(AppMenuRequestedEvent, value);
        remove => RemoveHandler(AppMenuRequestedEvent, value);
    }

    public Control AppMenuAnchor => AppMenuButton;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProjectTitleProperty)
        {
            ProjectTitleText.Text = ProjectTitle;
            ProjectTitleText.IsVisible = !string.IsNullOrEmpty(ProjectTitle);
        }
        else if (change.Property.OwnerType == typeof(TitleBar))
        {
            ApplyWindowButtonVisibility();
        }
    }

    private void ApplyWindowButtonVisibility()
    {
        bool captions = WindowChromePolicy.DrawsCaptionButtons;
        SettingsButton.IsVisible = ShowSettingsButton;
        AppMenuButton.IsVisible = ShowAppMenu;
        MinimizeButton.IsVisible = captions && ShowMinimizeButton;
        MaximizeButton.IsVisible = captions && ShowMaximizeButton;
        CloseButton.IsVisible = captions;
        // macOS draws the traffic lights at the top left; the system title already names the
        // window on Linux, so the in-window row keeps only the project title there.
        TitleContent.Margin = new Thickness(WindowChromePolicy.LeadingInset + 12, 0, 12, 0);
        AppIcon.IsVisible = !OperatingSystem.IsMacOS();
        AppTitleText.IsVisible = WindowChromePolicy.DrawsCaptionButtons || OperatingSystem.IsMacOS();
    }

    private void AttachWindow()
    {
        DetachWindow();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += Window_PropertyChanged;
            UpdateGlyph();
        }
    }

    private void DetachWindow()
    {
        if (_window is not null) _window.PropertyChanged -= Window_PropertyChanged;
        _window = null;
    }

    private void Window_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) UpdateGlyph();
    }

    private void UpdateGlyph()
    {
        bool maximized = _window?.WindowState == WindowState.Maximized;
        ToolTip.SetTip(MaximizeButton, maximized ? L.T("还原") : L.T("最大化"));
        MaximizeGlyph.IsVisible = !maximized;
        RestoreGlyph.IsVisible = maximized;
    }

    private void Bar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is not null && WindowChromePolicy.TitleBarMovesWindow
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
        {
            _window.BeginMoveDrag(e);
        }
    }

    private void ToggleMaximize()
    {
        if (_window is null) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        if (_window is not null) _window.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object? sender, RoutedEventArgs e) => _window?.Close();

    private void Settings_Click(object? sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(SettingsRequestedEvent, SettingsButton));

    private void AppMenu_Click(object? sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(AppMenuRequestedEvent, AppMenuButton));
}
