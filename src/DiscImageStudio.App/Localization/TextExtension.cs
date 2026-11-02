using Avalonia.Markup.Xaml;

namespace DiscImageStudio.Localization;

/// <summary>Localized text for XAML, including control themes and templates:
/// <c>{loc:Text Key}</c> with <c>xmlns:loc="using:DiscImageStudio.Localization"</c>.
/// Resolved when the element is created; the language is fixed for the session.</summary>
public sealed class TextExtension : MarkupExtension
{
    public TextExtension()
    {
    }

    public TextExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
