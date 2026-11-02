using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace DiscImageStudio.Controls;

public partial class PathField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Label), string.Empty);
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PathField, string?>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> ButtonLabelProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(ButtonLabel), "浏览");
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<PathField, ICommand?>(nameof(Command));
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<PathField, object?>(nameof(CommandParameter));
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<PathField, bool>(nameof(Compact));
    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Hint), string.Empty);
    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Placeholder), string.Empty);

    public PathField()
    {
        InitializeComponent();
        PathTextBox.Bind(TextBox.TextProperty, new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay });
        UpdateTexts();
        UpdateDensity();
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string ButtonLabel { get => GetValue(ButtonLabelProperty); set => SetValue(ButtonLabelProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty) BrowseButton.Command = Command;
        else if (change.Property == CommandParameterProperty) BrowseButton.CommandParameter = CommandParameter;
        else if (change.Property == CompactProperty) UpdateDensity();
        else if (change.Property == LabelProperty || change.Property == ButtonLabelProperty
                 || change.Property == HintProperty || change.Property == PlaceholderProperty) UpdateTexts();
    }

    private void UpdateTexts()
    {
        LabelText.Text = Label;
        LabelText.IsVisible = !string.IsNullOrWhiteSpace(Label);
        BrowseButton.Content = ButtonLabel;
        HintTextBlock.Text = Hint;
        HintTextBlock.IsVisible = !string.IsNullOrWhiteSpace(Hint);
        PathTextBox.PlaceholderText = Placeholder;
    }

    private void UpdateDensity()
    {
        double height = Compact ? 28 : 32;
        PathTextBox.Height = height;
        PathTextBox.MinHeight = height;
        PathTextBox.FontSize = Compact ? 13 : 14;
        BrowseButton.Height = height;
        BrowseButton.FontSize = 13;
    }
}
