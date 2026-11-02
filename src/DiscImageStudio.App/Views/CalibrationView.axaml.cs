using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DiscImageStudio.Hosting;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Views;

/// <summary>Calibration workspace: the photo stage beside the three-step column on wide pages
/// (the work area pinned to the viewport, the expanded step stretching), stacked and scrolling
/// below the 990 px breakpoint.</summary>
public partial class CalibrationView : UserControl
{
    private const double NarrowBreakpoint = 990;
    private const double StackedStageHeight = 420;
    private const double MinimumViewportForStretch = 100;
    private readonly CalibrationViewModel? _calibration;
    private bool _narrowLayout = true;

    public CalibrationView() => InitializeComponent();

    public CalibrationView(CalibrationViewModel calibration)
        : this()
    {
        _calibration = calibration;
        calibration.PropertyChanged += OnCalibrationPropertyChanged;
        DataContext = calibration;
        UpdateCanvasImage();
    }

    internal CalibrationPhotoCanvasHost Canvas => new(PhotoCanvas);

    private void OnViewSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void OnWorkspaceSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void OnWorkScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta != default || e.ViewportDelta != default) UpdateResponsiveLayout();
    }

    private void OnCalibrationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CalibrationViewModel.StepOneExpanded) or nameof(CalibrationViewModel.StepTwoExpanded)
            or nameof(CalibrationViewModel.StepThreeExpanded))
        {
            UpdateStepRows();
        }
        else if (e.PropertyName is nameof(CalibrationViewModel.PhotoImage) or nameof(CalibrationViewModel.RectifiedPhoto)
                 or nameof(CalibrationViewModel.IsTraceStep) or nameof(CalibrationViewModel.PreviewRectification))
        {
            UpdateCanvasImage();
        }
    }

    /// <summary>The raw photo while fitting boundaries; the rectified photo for the rectification
    /// preview and the trace step.</summary>
    private void UpdateCanvasImage()
    {
        if (_calibration is null) return;
        bool rectified = _calibration.IsTraceStep || _calibration.PreviewRectification;
        PhotoCanvas.Image = AvaloniaBitmaps.ToBitmap(rectified ? _calibration.RectifiedPhoto : _calibration.PhotoImage);
    }

    private void UpdateResponsiveLayout()
    {
        _narrowLayout = Bounds.Width < NarrowBreakpoint;
        ColumnDefinitions columns = WorkArea.ColumnDefinitions;
        RowDefinitions rows = WorkArea.RowDefinitions;
        if (_narrowLayout)
        {
            WorkArea.Height = double.NaN;
            rows[0].Height = GridLength.Auto;
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            Grid.SetColumn(StepsPanel, 0);
            Grid.SetRow(StepsPanel, 1);
            StepsPanel.Margin = new Thickness(0, 16, 0, 0);
            StageCard.Height = StackedStageHeight;
            UpdateStepRows();
            return;
        }

        rows[0].Height = new GridLength(1, GridUnitType.Star);
        columns[1].Width = new GridLength(16);
        columns[2].Width = new GridLength(ThemeValuesSidePanelWidth());
        Grid.SetColumn(StepsPanel, 2);
        Grid.SetRow(StepsPanel, 0);
        StepsPanel.Margin = default;
        StageCard.Height = double.NaN;
        double viewport = WorkScroll.Viewport.Height;
        WorkArea.Height = viewport > MinimumViewportForStretch ? viewport - 2 : double.NaN;
        UpdateStepRows();
    }

    private static double ThemeValuesSidePanelWidth() => Controls.ThemeValues.Double("SidePanelWidthValue", 256);

    private void UpdateStepRows()
    {
        RowDefinitions rows = StepsPanel.RowDefinitions;
        bool stretch = !_narrowLayout && _calibration is not null;
        rows[0].Height = ToRowHeight(stretch && _calibration!.StepOneExpanded);
        rows[1].Height = ToRowHeight(stretch && _calibration!.StepTwoExpanded);
        rows[2].Height = ToRowHeight(stretch && _calibration!.StepThreeExpanded);
    }

    private static GridLength ToRowHeight(bool expanded)
        => expanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

    private void OnZoomOut(object? sender, RoutedEventArgs e) => PhotoCanvas.ZoomBy(1 / 1.3);

    private void OnZoomIn(object? sender, RoutedEventArgs e) => PhotoCanvas.ZoomBy(1.3);

    private void OnResetView(object? sender, RoutedEventArgs e) => PhotoCanvas.ResetView();

    private void OnRemoveTracePoint(object? sender, RoutedEventArgs e) => PhotoCanvas.RemoveSelectedTracePoint();
}

/// <summary>Snapshot access to the calibration page's canvas.</summary>
internal readonly record struct CalibrationPhotoCanvasHost(Controls.CalibrationPhotoCanvas Canvas);
