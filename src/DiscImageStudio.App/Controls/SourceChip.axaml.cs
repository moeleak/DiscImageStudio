using Avalonia;
using Avalonia.Controls;

namespace DiscImageStudio.Controls;

public partial class SourceChip : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SourceChip, string>(nameof(Text), "来自设计页合成图");
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<SourceChip, object?>(nameof(Actions));

    public SourceChip()
    {
        InitializeComponent();
        ChipText.Text = Text;
        UpdateActions();
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) ChipText.Text = Text;
        else if (change.Property == ActionsProperty) UpdateActions();
    }

    private void UpdateActions()
    {
        ActionHost.Content = Actions;
        ActionHost.IsVisible = Actions is not null;
    }
}
