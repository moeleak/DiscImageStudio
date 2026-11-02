using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Views;

public partial class DesignView : UserControl
{
    // Below this page width the three-column arrangement leaves the canvas too narrow to work
    // on (at 720px it collapsed to nothing), so the panels move under it instead.
    private const double NarrowBreakpoint = 900;

    // Fixed heights for the stacked arrangement: the page scrolls as one surface there, so the
    // canvas and the layer list need explicit viewports rather than star rows.
    private const double StackedCanvasHeight = 380;
    private const double StackedLayersHeight = 260;
    private const double MinimumViewportForStretch = 100;

    private readonly DesignViewModel? _viewModel;
    private bool _layoutQueued;

    public DesignView() => InitializeComponent();

    public DesignView(DesignViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        _viewModel = viewModel;
        // Page-level keys: the nudge must work wherever the focus sits (the layer list, a
        // button, the canvas), so it is a tunnelling handler rather than the canvas's KeyDown.
        AddHandler(KeyDownEvent, OnPagePreviewKeyDown, RoutingStrategies.Tunnel);
    }

    internal Controls.DiscDesignCanvas Canvas => DesignCanvas;

    /// <summary>Arrow keys nudge the selected layer by 1 mm; Delete removes it and Escape
    /// clears the selection. Text fields, sliders and the segmented control keep their own
    /// editing keys, so a coordinate being typed never moves the layer underneath the caret
    /// and a focused slider still responds to its arrows.</summary>
    private void OnPagePreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null
            || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or RangeBase or RadioButton)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down
                when _viewModel.SelectedLayer is { } layer:
            {
                // 1 mm per press; rounding keeps repeated presses from accumulating binary
                // noise in the mm coordinates.
                double dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
                double dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
                layer.TranslateXMm = Math.Round(layer.TranslateXMm + dx, 2);
                layer.TranslateYMm = Math.Round(layer.TranslateYMm + dy, 2);
                e.Handled = true;
                break;
            }
            case Key.Delete when _viewModel.SelectedLayer is not null:
                _viewModel.RemoveSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when _viewModel.SelectedLayer is not null:
                _viewModel.SelectedLayer = null;
                e.Handled = true;
                break;
        }
    }

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateResponsiveLayout();

    private void OnWorkScrollSizeChanged(object? sender, SizeChangedEventArgs e) => QueueResponsiveLayout();

    private void OnFilesCardSizeChanged(object? sender, SizeChangedEventArgs e) => QueueResponsiveLayout();

    private void ZoomInClick(object? sender, RoutedEventArgs e) => DesignCanvas.ZoomIn();

    private void ZoomOutClick(object? sender, RoutedEventArgs e) => DesignCanvas.ZoomOut();

    private void ResetCanvasZoomClick(object? sender, RoutedEventArgs e) => DesignCanvas.ResetView();

    // Viewport or extent size changes re-run the pin: the first arrange can report a zero
    // viewport, and fractional-DPI rounding can leave the pinned content a pixel taller than
    // the viewport — scrolling back and forth must never be the permanent state of the page.
    private void OnWorkScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta != default || e.ViewportDelta != default)
        {
            QueueResponsiveLayout();
        }
    }

    // Viewport sizes are only final after the arrange pass that raised the event, so the pin
    // runs once on the next dispatcher turn instead of re-entering layout.
    private void QueueResponsiveLayout()
    {
        if (_layoutQueued)
        {
            return;
        }

        _layoutQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _layoutQueued = false;
            UpdateResponsiveLayout();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    // Wide: the workspace is exactly the viewport tall — the canvas fills it and both rails
    // scroll inside their own columns. Narrow: canvas, layers/files and the property/tone rail stack
    // and the page scrolls; the canvas keeps a real size instead of being squeezed between the
    // panels.
    private void UpdateResponsiveLayout()
    {
        ColumnDefinitions columns = Workspace.ColumnDefinitions;
        RowDefinitions rows = Workspace.RowDefinitions;
        if (Bounds.Width < NarrowBreakpoint)
        {
            Workspace.Height = double.NaN;
            LeftRail.MinHeight = 0;
            RightRail.MinHeight = 0;
            ToneRail.RailHeightBudget = null;
            rows[0].Height = GridLength.Auto;
            CanvasCard.Height = StackedCanvasHeight;
            LayersCard.MaxHeight = double.PositiveInfinity;
            LayersCard.Height = StackedLayersHeight;
            LeftRailScroll.Margin = new Thickness(0, 16, 0, 0);
            LeftRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            LeftRailScroll.Offset = default;
            columns[0].Width = new GridLength(1, GridUnitType.Star);
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            columns[3].Width = new GridLength(0);
            columns[4].Width = new GridLength(0);
            Grid.SetColumn(CanvasCard, 0);
            Grid.SetColumnSpan(CanvasCard, 5);
            Grid.SetColumn(LeftRailScroll, 0);
            Grid.SetColumnSpan(LeftRailScroll, 5);
            Grid.SetRow(LeftRailScroll, 1);
            // The rail becomes part of the page's single scroll surface: its own scrollbar
            // would nest a second viewport inside a page that is already scrolling.
            RightRailScroll.Margin = new Thickness(0, 16, 0, 0);
            RightRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            RightRailScroll.Offset = default;
            Grid.SetColumn(RightRailScroll, 0);
            Grid.SetColumnSpan(RightRailScroll, 5);
            Grid.SetRow(RightRailScroll, 2);
            return;
        }

        double sidePanelWidth = Controls.ThemeValues.Double("SidePanelWidthValue", 256);
        rows[0].Height = new GridLength(1, GridUnitType.Star);
        CanvasCard.Height = double.NaN;
        LayersCard.Height = double.NaN;
        LeftRailScroll.Margin = default;
        LeftRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RightRailScroll.Margin = default;
        RightRailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        columns[0].Width = new GridLength(sidePanelWidth);
        columns[1].Width = new GridLength(16);
        columns[2].Width = new GridLength(1, GridUnitType.Star);
        columns[3].Width = new GridLength(16);
        columns[4].Width = new GridLength(sidePanelWidth);
        Grid.SetColumn(CanvasCard, 2);
        Grid.SetColumnSpan(CanvasCard, 1);
        Grid.SetColumn(LeftRailScroll, 0);
        Grid.SetColumnSpan(LeftRailScroll, 1);
        Grid.SetRow(LeftRailScroll, 0);
        Grid.SetColumn(RightRailScroll, 4);
        Grid.SetColumnSpan(RightRailScroll, 1);
        Grid.SetRow(RightRailScroll, 0);

        // Star rows cannot size inside a scroll viewer, so the work area is pinned to the
        // viewport height while the wide arrangement is active.
        double viewport = WorkScroll.Viewport.Height;
        Workspace.Height = viewport > MinimumViewportForStretch ? viewport - 2 : double.NaN;
        double leftViewport = LeftRailScroll.Viewport.Height;
        LeftRail.MinHeight = leftViewport > MinimumViewportForStretch ? leftViewport : 0;
        // A ScrollViewer measures with infinite height, so the star row alone cannot keep
        // the ListBox in a viewport. Bound the layer card to the room left by the file
        // controls; the list then scrolls internally instead of pushing the picker below
        // every layer. Very short windows can still scroll the whole rail.
        double fileControlsHeight = FilesCardGate.IsVisible ? FilesCardGate.Bounds.Height : 0;
        LayersCard.MaxHeight = Math.Max(StackedLayersHeight, leftViewport - fileControlsHeight);
        // The rail matches the viewport when its content is shorter: the tone card's star
        // row absorbs the slack so the column ends flush with the canvas instead of at a
        // ragged natural height. A taller rail keeps its natural height and scrolls. The
        // budget tells the tone module how much of that height it may consume, so the disc
        // preview shrinks before the column would spill into a scrollbar.
        double rightViewport = RightRailScroll.Viewport.Height;
        RightRail.MinHeight = rightViewport > MinimumViewportForStretch ? rightViewport : 0;
        if (rightViewport > MinimumViewportForStretch && ToneCard.Bounds.Height > 0)
        {
            double toneChrome = ToneCard.Padding.Top + ToneCard.Padding.Bottom
                + ToneCard.BorderThickness.Top + ToneCard.BorderThickness.Bottom;
            ToneRail.RailHeightBudget = rightViewport - PropsCard.Bounds.Height - ToneCard.Margin.Top - toneChrome;
        }
        else
        {
            ToneRail.RailHeightBudget = null;
        }
    }
}
