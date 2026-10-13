using System.Text.Json;
using System.IO;
using System.Windows;

namespace Nexora.Desktop.Services;

public enum AppearanceTheme
{
    Dark,
    Light
}

public sealed class ThemeService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nexora", "settings.json");

    public AppearanceTheme CurrentTheme { get; private set; } = AppearanceTheme.Dark;

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(_settingsPath));
                if (settings?.Theme is not null && Enum.TryParse<AppearanceTheme>(settings.Theme, true, out var theme))
                    CurrentTheme = theme;
            }
        }
        catch
        {
            CurrentTheme = AppearanceTheme.Dark;
        }
    }

    public void Apply(AppearanceTheme theme)
    {
        CurrentTheme = theme;
        if (Application.Current is null) return;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Colors.") == true);
        if (colors is not null) dictionaries.Remove(colors);
        dictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri(theme == AppearanceTheme.Light ? "Resources/Colors.Light.xaml" : "Resources/Colors.Dark.xaml", UriKind.Relative)
        });
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new ThemeSettings(theme.ToString())));
        }
        catch
        {
            // A theme change remains valid for this session if local preferences cannot be written.
        }
    }

    private sealed record ThemeSettings(string Theme);
}
