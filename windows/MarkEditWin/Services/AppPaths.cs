using System.IO;

namespace MarkEditWin.Services;

/// <summary>
/// Well known locations used by MarkEdit for Windows.
///
/// User customization lives in <c>Documents\MarkEdit</c> so it is easy to find and edit
/// (settings.json, editor.css, editor.js, scripts), mirroring the macOS layout.
/// </summary>
public static class AppPaths
{
    public const string AppName = "MarkEdit";

    public static string UserDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        AppName);

    public static string SettingsFile { get; } = Path.Combine(UserDataDirectory, "settings.json");

    public static string CustomCssFile { get; } = Path.Combine(UserDataDirectory, "editor.css");

    public static string CustomScriptFile { get; } = Path.Combine(UserDataDirectory, "editor.js");

    public static string ScriptsDirectory { get; } = Path.Combine(UserDataDirectory, "scripts");

    public static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName,
        "Cache");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(ScriptsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
