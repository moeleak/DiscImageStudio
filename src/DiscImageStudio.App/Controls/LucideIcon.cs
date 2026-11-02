using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DiscImageStudio.Controls;

/// <summary>Lucide glyph by name (e.g. <c>Kind="Layers"</c>), rendered like a PathIcon:
/// filled with <see cref="TemplatedControl.Foreground"/>, stretched into Width × Height.</summary>
public class LucideIcon : PathIcon
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<LucideIcon, string?>(nameof(Kind));

    static LucideIcon()
    {
        KindProperty.Changed.AddClassHandler<LucideIcon>((icon, _) => icon.UpdateData());
    }

    // Styled as a PathIcon: 16 px by default through IconElementThemeWidth/Height in App.axaml
    // (never set sizes in the constructor; a local value would outrank template sizes).
    // Selectors match the style key, so target these icons as "PathIcon#Name", never by the
    // LucideIcon type.
    protected override Type StyleKeyOverride => typeof(PathIcon);

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static Geometry? Lookup(string? kind)
    {
        if (string.IsNullOrEmpty(kind) || kind == "None" || Application.Current is not { } application)
        {
            return null;
        }

        return application.TryGetResource("Icon." + kind, null, out object? value) ? value as Geometry : null;
    }

    private void UpdateData() => Data = Lookup(Kind);
}
