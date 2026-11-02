using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace DiscImageStudio.Controls;

public partial class InterleaveToggle : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<InterleaveToggle, string>(nameof(Label), "启用 CD-DA 延迟交织");
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<InterleaveToggle, bool>(nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<InterleaveToggle, string>(nameof(Hint),
            "如您在盘片制作时勾选了“启用 CD-DA 延迟交织”，则应勾选该选项。");

    public InterleaveToggle()
    {
        InitializeComponent();
        Toggle.Content = Label;
        Toggle.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(IsChecked)) { Source = this, Mode = BindingMode.TwoWay });
        HintSpot.Hint = Hint;
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsChecked { get => GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty) Toggle.Content = Label;
        else if (change.Property == HintProperty) HintSpot.Hint = Hint;
    }
}
