using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MarkEditWin.Services;

/// <summary>
/// Reads and writes <c>Documents\MarkEdit\settings.json</c>.
///
/// The shared <c>editor.*</c> and <c>general.*</c> sections follow the macOS schema so that
/// one settings file can be used on both platforms; Windows-only preferences live under
/// <c>windows.*</c>.
/// </summary>
public sealed class AppSettings
{
    private const string DefaultContents = """
    {
      "editor": {
        "autoCharacterPairs": true,
        "autoSaveWhenIdle": false,
        "indentBehavior": "never"
      },
      "general": {
        "defaultOpenDirectory": "",
        "defaultSaveDirectory": ""
      },
      "windows": {
        "theme": "auto",
        "fontFamily": "Cascadia Mono",
        "fontSize": 17,
        "showLineNumbers": true,
        "showActiveLineIndicator": true,
        "lineWrapping": true,
        "lineHeight": 1.5,
        "invisiblesBehavior": "selection",
        "typewriterMode": false,
        "focusMode": false
      }
    }
    """;

    private JsonObject _root = new();
    private JsonObject? _editor;
    private JsonObject? _general;
    private JsonObject? _windows;

    public AppSettings()
    {
        Reload();
    }

    public void Reload()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.SettingsFile))
            {
                File.WriteAllText(AppPaths.SettingsFile, DefaultContents);
            }

            _root = JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile)) as JsonObject ?? new JsonObject();
        }
        catch (Exception)
        {
            _root = new JsonObject();
        }

        _editor = _root["editor"] as JsonObject;
        _general = _root["general"] as JsonObject;
        _windows = _root["windows"] as JsonObject;
    }

    /// <summary>Raw contents, handed to the page as <c>MarkEdit.userSettings</c>.</summary>
    public string RawJson
    {
        get
        {
            try
            {
                return File.Exists(AppPaths.SettingsFile) ? File.ReadAllText(AppPaths.SettingsFile) : "{}";
            }
            catch (Exception)
            {
                return "{}";
            }
        }
    }

    public bool EditorAutoCharacterPairs => GetBool(_editor, "autoCharacterPairs", true);

    public bool EditorAutoSaveWhenIdle => GetBool(_editor, "autoSaveWhenIdle", false);

    public string EditorIndentBehavior => GetString(_editor, "indentBehavior", "never");

    public double? EditorUndoGroupingInterval => GetDouble(_editor, "undoGroupingInterval");

    public double[]? EditorHeaderFontSizeDiffs => GetDoubleArray(_editor, "headerFontSizeDiffs");

    public string? EditorVisibleWhitespaceCharacter => GetStringOrNull(_editor, "visibleWhitespaceCharacter");

    public string? EditorVisibleLineBreakCharacter => GetStringOrNull(_editor, "visibleLineBreakCharacter");

    public Dictionary<string, string>? EditorSearchNormalizers
    {
        get
        {
            if (_editor?["searchNormalizers"] is not JsonObject normalizers)
            {
                return null;
            }

            var result = new Dictionary<string, string>();
            foreach (var pair in normalizers)
            {
                if (pair.Value is not null)
                {
                    result[pair.Key] = pair.Value.ToString();
                }
            }

            return result.Count == 0 ? null : result;
        }
    }

    public string DefaultOpenDirectory
    {
        get
        {
            var value = GetStringOrNull(_general, "defaultOpenDirectory");
            return string.IsNullOrWhiteSpace(value) || !Directory.Exists(value)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : value;
        }
    }

    public string DefaultSaveDirectory
    {
        get
        {
            var value = GetStringOrNull(_general, "defaultSaveDirectory");
            return string.IsNullOrWhiteSpace(value) || !Directory.Exists(value)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : value;
        }
    }

    // MARK: - Windows preferences

    public string Theme
    {
        get => GetString(_windows, "theme", "auto");
        set => Set(_windows, "theme", value);
    }

    public string FontFamily
    {
        get => GetString(_windows, "fontFamily", "Cascadia Mono");
        set => Set(_windows, "fontFamily", value);
    }

    public double FontSize
    {
        get => GetDouble(_windows, "fontSize") ?? 17;
        set => Set(_windows, "fontSize", value);
    }

    public bool ShowLineNumbers
    {
        get => GetBool(_windows, "showLineNumbers", true);
        set => Set(_windows, "showLineNumbers", value);
    }

    public bool ShowActiveLineIndicator
    {
        get => GetBool(_windows, "showActiveLineIndicator", true);
        set => Set(_windows, "showActiveLineIndicator", value);
    }

    public bool LineWrapping
    {
        get => GetBool(_windows, "lineWrapping", true);
        set => Set(_windows, "lineWrapping", value);
    }

    public double LineHeight
    {
        get => GetDouble(_windows, "lineHeight") ?? 1.5;
        set => Set(_windows, "lineHeight", value);
    }

    public string InvisiblesBehavior
    {
        get => GetString(_windows, "invisiblesBehavior", "selection");
        set => Set(_windows, "invisiblesBehavior", value);
    }

    public bool TypewriterMode
    {
        get => GetBool(_windows, "typewriterMode", false);
        set => Set(_windows, "typewriterMode", value);
    }

    public bool FocusMode
    {
        get => GetBool(_windows, "focusMode", false);
        set => Set(_windows, "focusMode", value);
    }

    public string[] RecentFiles
    {
        get
        {
            if (_windows?["recentFiles"] is not JsonArray array)
            {
                return [];
            }

            return array
                .Where(node => node is not null)
                .Select(node => node!.GetValue<string>())
                .ToArray();
        }
        set
        {
            _windows ??= EnsureWindowsSection();
            var array = new JsonArray();
            foreach (var item in value)
            {
                array.Add(item);
            }

            _windows["recentFiles"] = array;
        }
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(AppPaths.SettingsFile, _root.ToJsonString(options));
        }
        catch (Exception)
        {
            // Settings are best effort, never block the editor on a write failure.
        }
    }

    private JsonObject EnsureWindowsSection()
    {
        if (_root["windows"] is JsonObject existing)
        {
            _windows = existing;
            return existing;
        }

        var created = new JsonObject();
        _root["windows"] = created;
        _windows = created;
        return created;
    }

    private void Set(JsonObject? section, string key, object value)
    {
        section ??= EnsureWindowsSection();
        section[key] = JsonValue.Create(value);
    }

    private static string GetString(JsonObject? section, string key, string fallback)
    {
        return GetStringOrNull(section, key) ?? fallback;
    }

    private static string? GetStringOrNull(JsonObject? section, string key)
    {
        if (section?[key] is JsonNode node && node.GetValueKind() == JsonValueKind.String)
        {
            return node.GetValue<string>();
        }

        return null;
    }

    private static bool GetBool(JsonObject? section, string key, bool fallback)
    {
        if (section?[key] is JsonNode node && node.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            return node.GetValue<bool>();
        }

        return fallback;
    }

    private static double? GetDouble(JsonObject? section, string key)
    {
        if (section?[key] is JsonNode node && node.GetValueKind() == JsonValueKind.Number)
        {
            return node.GetValue<double>();
        }

        return null;
    }

    private static double[]? GetDoubleArray(JsonObject? section, string key)
    {
        if (section?[key] is not JsonArray array)
        {
            return null;
        }

        var result = new List<double>();
        foreach (var node in array)
        {
            if (node is not null && node.GetValueKind() == JsonValueKind.Number)
            {
                result.Add(node.GetValue<double>());
            }
        }

        return result.Count == 0 ? null : result.ToArray();
    }
}
