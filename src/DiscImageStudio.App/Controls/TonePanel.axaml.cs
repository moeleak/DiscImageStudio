using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

public partial class TonePanel : UserControl
{
    private const double WideBreakpoint = 520;
    private const double CompactDiscSize = 128;
    private const double WideDiscSize = 200;
    private const double WidePreviewColumn = 240;
    private const double PreviewCardChrome = 22;
    private double? _railHeightBudget;

    public TonePanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    /// <summary>Height the design page's rail can give the module; the narrow medallion grows
    /// into whatever the controls leave.</summary>
    public double? RailHeightBudget
    {
        get => _railHeightBudget;
        set
        {
            if (_railHeightBudget == value) return;
            _railHeightBudget = value;
            UpdateResponsiveLayout();
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (Bounds.Width <= 0) return;
        ColumnDefinitions columns = ToneLayout.ColumnDefinitions;
        if (Bounds.Width >= WideBreakpoint)
        {
            columns[0].Width = new GridLength(WidePreviewColumn);
            columns[1].Width = new GridLength(20);
            columns[2].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(ToneControls, 1);
            Grid.SetColumn(ToneControls, 2);
            ToneControls.Margin = default;
            PreviewImageHost.Width = WideDiscSize;
            PreviewImageHost.Height = WideDiscSize;
            return;
        }

        columns[0].Width = new GridLength(1, GridUnitType.Star);
        columns[1].Width = new GridLength(0);
        columns[2].Width = new GridLength(0);
        Grid.SetRow(ToneControls, 2);
        Grid.SetColumn(ToneControls, 0);
        ToneControls.Margin = new Thickness(0, 14, 0, 0);
        double width = Math.Floor(ToneLayout.Bounds.Width - PreviewCardChrome);
        double disc = width;
        if (RailHeightBudget is double budget)
        {
            double reserved = ModuleTitle.Bounds.Height + ModuleTitle.Margin.Top + ModuleTitle.Margin.Bottom
                + ToneControls.Bounds.Height + ToneControls.Margin.Top + PreviewCardChrome;
            disc = Math.Min(width, Math.Floor(Math.Max(CompactDiscSize, budget - reserved)));
        }

        PreviewImageHost.Width = Math.Max(CompactDiscSize, disc);
        PreviewImageHost.Height = Math.Max(CompactDiscSize, disc);
    }
}
