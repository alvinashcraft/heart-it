using System.Windows;
using Microsoft.Win32;

namespace HeartIt;

/// <summary>
/// Interaction logic for App.xaml. Detects the current Windows light/dark theme and
/// swaps the custom theme dictionary so app-specific brushes match the system theme.
/// The Fluent theme itself is handled automatically by <c>ThemeMode="System"</c>.
/// </summary>
public partial class App : Application
{
    private static readonly Uri LightThemeUri = new("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
    private static readonly Uri DarkThemeUri = new("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyTheme(IsSystemDarkMode());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyTheme(IsSystemDarkMode())));
        }
    }

    private void ApplyTheme(bool dark)
    {
        var targetUri = dark ? DarkThemeUri : LightThemeUri;
        var dictionaries = Resources.MergedDictionaries;

        for (int i = 0; i < dictionaries.Count; i++)
        {
            var src = dictionaries[i].Source;
            if (src is null) continue;
            if (src == LightThemeUri || src == DarkThemeUri)
            {
                if (src == targetUri) return;
                dictionaries[i] = new ResourceDictionary { Source = targetUri };
                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = targetUri });
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0;
            }
        }
        catch
        {
            // Registry access can fail in restricted contexts; fall back to light.
        }
        return false;
    }
}
