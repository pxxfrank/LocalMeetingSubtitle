using System.Linq;
using System.Windows;

namespace LocalMeetingSubtitle.App.Infrastructure;

/// <summary>
/// Swaps the active Claude theme dictionary at runtime. The two theme dictionaries
/// (Claude.Light / Claude.Dark) define the same keys, so replacing one with the other
/// live-updates every {DynamicResource} in the UI.
/// </summary>
public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    private const string ThemePathPrefix = "Theme/Claude.";

    /// <summary>The currently applied theme name ("Light" or "Dark").</summary>
    public static string Current { get; private set; } = Light;

    /// <summary>
    /// Removes any previously merged theme dictionary and merges the requested one in its place.
    /// Never throws: an unknown or missing theme falls back to Light.
    /// </summary>
    public static void Apply(string? theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var normalized = string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;
        var dictionaries = app.Resources.MergedDictionaries;

        // Drop the currently merged theme dictionary (matched by its source pack URI).
        foreach (var existing in dictionaries
                     .Where(d => d.Source is not null &&
                                 d.Source.OriginalString.Contains(ThemePathPrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            dictionaries.Remove(existing);
        }

        var dictionary = TryLoad(normalized);
        if (dictionary is null && normalized != Light)
        {
            normalized = Light;
            dictionary = TryLoad(Light);
        }

        if (dictionary is null)
        {
            // Nothing could be loaded (unexpected); keep whatever the app already has.
            return;
        }

        // Insert first so the control styles and icons stay after the theme.
        dictionaries.Insert(0, dictionary);
        Current = normalized;
    }

    private static ResourceDictionary? TryLoad(string theme)
    {
        try
        {
            return new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Theme/Claude.{theme}.xaml", UriKind.Absolute)
            };
        }
        catch
        {
            return null;
        }
    }
}
