using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DiscImageStudio.Controls;

/// <summary>Two- or three-way segmented selector. <see cref="SelectedIndex"/> and
/// <see cref="IsSecondSelected"/> both bind two-way.</summary>
public partial class SegmentedControl : UserControl
{
    public static readonly StyledProperty<string> LabelAProperty =
        AvaloniaProperty.Register<SegmentedControl, string>(nameof(LabelA), "CD");
    public static readonly StyledProperty<string> LabelBProperty =
        AvaloniaProperty.Register<SegmentedControl, string>(nameof(LabelB), "DVD");
    public static readonly StyledProperty<string> LabelCProperty =
        AvaloniaProperty.Register<SegmentedControl, string>(nameof(LabelC), string.Empty);
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedControl, int>(nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsSecondSelectedProperty =
        AvaloniaProperty.Register<SegmentedControl, bool>(nameof(IsSecondSelected), defaultBindingMode: BindingMode.TwoWay);

    private bool _updatingSelection;
    private bool _realized;

    public SegmentedControl()
    {
        InitializeComponent();
        Cells.SizeChanged += (_, _) => ApplyLayout(animate: false);
        ApplyLayout(animate: false);
    }

    public string LabelA { get => GetValue(LabelAProperty); set => SetValue(LabelAProperty, value); }
    public string LabelB { get => GetValue(LabelBProperty); set => SetValue(LabelBProperty, value); }
    public string LabelC { get => GetValue(LabelCProperty); set => SetValue(LabelCProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public bool IsSecondSelected { get => GetValue(IsSecondSelectedProperty); set => SetValue(IsSecondSelectedProperty, value); }

    private bool HasThird => !string.IsNullOrWhiteSpace(LabelC);

    private int OptionCount => HasThird ? 3 : 2;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty)
        {
            int index = Math.Clamp(SelectedIndex, 0, OptionCount - 1);
            if (index != SelectedIndex)
            {
                SelectedIndex = index;
                return;
            }

            _updatingSelection = true;
            try { IsSecondSelected = index == 1; }
            finally { _updatingSelection = false; }
            ApplyLayout(animate: true);
        }
        else if (change.Property == IsSecondSelectedProperty)
        {
            if (!_updatingSelection) SelectedIndex = IsSecondSelected ? 1 : 0;
        }
        else if (change.Property == LabelAProperty || change.Property == LabelBProperty || change.Property == LabelCProperty)
        {
            ApplyLayout(animate: false);
        }
    }

    private void ApplyLayout(bool animate)
    {
        OptionA.Content = LabelA;
        OptionB.Content = LabelB;
        OptionC.Content = LabelC;
        bool hasThird = HasThird;
        Cells.ColumnDefinitions[2].Width = hasThird ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        OptionC.IsVisible = hasThird;
        int index = Math.Clamp(SelectedIndex, 0, OptionCount - 1);
        OptionA.Classes.Set("selected", index == 0);
        OptionB.Classes.Set("selected", index == 1);
        OptionC.Classes.Set("selected", index == 2);
        double width = Cells.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        double cell = width / OptionCount;
        Thumb.Transitions = animate && _realized
            ? [new ThicknessTransition { Property = MarginProperty, Duration = Motion.Base, Easing = Motion.EaseOut }]
            : null;
        Thumb.Width = Math.Max(0, cell - 6);
        Thumb.Margin = new Thickness(3 + cell * index, 3, 3, 3);
        _realized = true;
    }

    private void OptionClick(object? sender, RoutedEventArgs e)
        => SelectedIndex = sender == OptionA ? 0 : sender == OptionB ? 1 : 2;

    private void OptionKeyDown(object? sender, KeyEventArgs e)
    {
        int current = sender == OptionA ? 0 : sender == OptionB ? 1 : 2;
        int next = e.Key switch
        {
            Key.Left => Math.Max(0, current - 1),
            Key.Right => Math.Min(OptionCount - 1, current + 1),
            _ => current,
        };
        if (next == current) return;
        e.Handled = true;
        SelectedIndex = next;
        (next == 0 ? OptionA : next == 1 ? OptionB : OptionC).Focus();
    }
}
