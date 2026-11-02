using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

public partial class HelpHint : UserControl
{
    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<HelpHint, string>(nameof(Hint), string.Empty);

    public HelpHint()
    {
        InitializeComponent();
        UpdateHint();
    }

    public string Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HintProperty) UpdateHint();
    }

    private void UpdateHint() => ToolTip.SetTip(Root, string.IsNullOrEmpty(Hint) ? null : Hint);
}
