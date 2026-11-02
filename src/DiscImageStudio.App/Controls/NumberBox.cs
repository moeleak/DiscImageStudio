using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace DiscImageStudio.Controls;

/// <summary>A text box bound to a <see cref="double"/>. Typing updates <see cref="Value"/> as soon
/// as the text parses, without reformatting under the caret; the text is reformatted with
/// <see cref="Format"/> when the value changes from elsewhere or the box loses focus. This is
/// the behaviour the WPF fields had with <c>StringFormat</c> and <c>UpdateSourceTrigger=PropertyChanged</c>,
/// which Avalonia's string-format converter does not support for two-way bindings.</summary>
public class NumberBox : TextBox
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberBox, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> FormatProperty =
        AvaloniaProperty.Register<NumberBox, string?>(nameof(Format));

    private bool _updatingFromText;
    private bool _updatingText;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Standard numeric format string such as <c>F2</c>; null uses the shortest round-trip form.</summary>
    public string? Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == FormatProperty)
        {
            if (!_updatingFromText)
            {
                SyncText();
            }
        }
        else if (change.Property == TextProperty && !_updatingText)
        {
            if (TryParse(Text, out double value))
            {
                _updatingFromText = true;
                try
                {
                    SetCurrentValue(ValueProperty, value);
                }
                finally
                {
                    _updatingFromText = false;
                }
            }
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        SyncText();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncText();
    }

    private void SyncText()
    {
        string text = string.IsNullOrEmpty(Format)
            ? Value.ToString("R", CultureInfo.CurrentCulture)
            : Value.ToString(Format, CultureInfo.CurrentCulture);
        if (Text == text)
        {
            return;
        }

        _updatingText = true;
        try
        {
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _updatingText = false;
        }
    }

    private static bool TryParse(string? text, out double value)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        return double.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
    }
}
