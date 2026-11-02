using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>Guards instant theme switching: the Light theme dictionary must define every key of
/// the Dark one with the same element type (and nothing else), and no view may hold a palette
/// resource through StaticResource, which would freeze the value of the theme it loaded under.</summary>
internal static class ThemePaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly Regex StaticResourceReference = new(
        @"\{StaticResource\s+([A-Za-z0-9_]+)\s*\}",
        RegexOptions.CultureInvariant);

    public static void Run()
    {
        string repositoryRoot = FindRepositoryRoot();
        string appRoot = Path.Combine(repositoryRoot, "src", "DiscImageStudio.App");
        string tokensPath = Path.Combine(appRoot, "Themes", "Tokens.axaml");

        XDocument document = XDocument.Load(tokensPath);
        XElement themeDictionaries = document.Root!.Element(Avalonia + "ResourceDictionary.ThemeDictionaries")
            ?? throw new InvalidOperationException("Theme palette check failed: Tokens.axaml has no ThemeDictionaries.");
        Dictionary<string, string> dark = ReadEntries(themeDictionaries, "Dark");
        Dictionary<string, string> light = ReadEntries(themeDictionaries, "Light");
        Require(dark.Count > 0, "the dark theme dictionary exposes palette keys");

        foreach ((string key, string element) in dark)
        {
            Require(light.TryGetValue(key, out string? lightElement), "light palette overrides " + key);
            Require(lightElement == element, "light palette keeps the element type of " + key);
        }

        foreach (string key in light.Keys)
        {
            Require(dark.ContainsKey(key), "light palette does not invent the key " + key);
        }

        foreach (string file in EnumerateViewXaml(appRoot))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in StaticResourceReference.Matches(text))
            {
                string key = match.Groups[1].Value;
                Require(!dark.ContainsKey(key),
                    Path.GetFileName(file) + " binds " + key
                    + " with StaticResource; palette references must use DynamicResource");
            }
        }

        Console.WriteLine($"theme-palette: passed ({dark.Count} keys in both themes, views use DynamicResource)");
    }

    private static Dictionary<string, string> ReadEntries(XElement themeDictionaries, string theme)
    {
        XElement dictionary = themeDictionaries.Elements(Avalonia + "ResourceDictionary")
            .SingleOrDefault(element => (string?)element.Attribute(Xaml + "Key") == theme)
            ?? throw new InvalidOperationException($"Theme palette check failed: no {theme} theme dictionary.");
        Dictionary<string, string> entries = new(StringComparer.Ordinal);
        foreach (XElement element in dictionary.Elements())
        {
            XAttribute? key = element.Attribute(Xaml + "Key");
            if (key is not null)
            {
                Require(entries.TryAdd(key.Value, element.Name.LocalName), $"{theme} defines {key.Value} once");
            }
        }

        return entries;
    }

    private static IEnumerable<string> EnumerateViewXaml(string appRoot)
    {
        foreach (string file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(appRoot, file);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Any(segment =>
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Path.GetFileName(file).Equals("Tokens.axaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DiscImageStudio.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Theme palette check failed: cannot locate DiscImageStudio.slnx above the test output folder.");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Theme palette check failed: " + name);
        }
    }
}
