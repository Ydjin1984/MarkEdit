using Microsoft.Win32;

namespace MarkEditWin.Services;

/// <summary>
/// Tracks the Windows light/dark preference so the editor theme can follow the system.
/// </summary>
public static class NativeTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static event Action? Changed;

    private static bool _subscribed;

    public static bool IsDarkMode
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key?.GetValue("AppsUseLightTheme") is int value)
                {
                    return value == 0;
                }
            }
            catch (Exception)
            {
                // Registry is unavailable in restricted environments, assume light.
            }

            return false;
        }
    }

    public static string ResolveTheme(string preference)
    {
        if (!string.IsNullOrWhiteSpace(preference) && !string.Equals(preference, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return preference;
        }

        return IsDarkMode ? "github-dark" : "github-light";
    }

    public static void StartObserving()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        SystemEvents.UserPreferenceChanged += (_, _) => Changed?.Invoke();
    }
}
