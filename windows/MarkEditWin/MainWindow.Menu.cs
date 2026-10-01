using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarkEditWin.Services;
using MarkEditWin.Views;
using Microsoft.Web.WebView2.Core;

namespace MarkEditWin;

/// <summary>
/// Menu construction, keyboard shortcut table and context menus.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Shortcuts handled inside the page. The WebView2 owns the keyboard while the editor has
    /// focus, so these are matched there and forwarded back as commands. The same list is also
    /// registered with WPF so the shortcuts keep working while the find bar has focus.
    /// </summary>
    private static readonly Shortcut[] Shortcuts =
    [
        new("s", Ctrl: true, Command: "file.save"),
        new("s", Ctrl: true, Shift: true, Command: "file.saveAs"),
        new("o", Ctrl: true, Command: "file.open"),
        new("n", Ctrl: true, Command: "file.new"),
        new("n", Ctrl: true, Shift: true, Command: "window.new"),
        new("w", Ctrl: true, Command: "file.close"),
        new("r", Ctrl: true, Command: "file.revert"),

        new("f", Ctrl: true, Command: "edit.find"),
        new("h", Ctrl: true, Command: "edit.replace"),
        new("F3", Command: "edit.findNext"),
        new("F3", Shift: true, Command: "edit.findPrevious"),
        new("e", Ctrl: true, Command: "edit.findSelection"),
        new("d", Ctrl: true, Command: "edit.selectNextOccurrence"),
        new("e", Ctrl: true, Shift: true, Command: "edit.selectAllOccurrences"),
        new("g", Ctrl: true, Command: "edit.gotoLine"),
        new("t", Ctrl: true, Command: "edit.toc"),

        new("b", Ctrl: true, Command: "format.bold"),
        new("i", Ctrl: true, Command: "format.italic"),
        new("x", Ctrl: true, Shift: true, Command: "format.strikethrough"),
        new("1", Ctrl: true, Command: "format.heading1"),
        new("2", Ctrl: true, Command: "format.heading2"),
        new("3", Ctrl: true, Command: "format.heading3"),
        new("4", Ctrl: true, Command: "format.heading4"),
        new("5", Ctrl: true, Command: "format.heading5"),
        new("6", Ctrl: true, Command: "format.heading6"),
        new("k", Ctrl: true, Command: "format.link"),
        new("/", Ctrl: true, Command: "format.toggleLineComment"),
        new("a", Ctrl: true, Shift: true, Command: "format.toggleBlockComment"),
        new("[", Ctrl: true, Command: "format.indentLess"),
        new("]", Ctrl: true, Command: "format.indentMore"),
        new("l", Ctrl: true, Shift: true, Command: "format.bullet"),
        new("o", Ctrl: true, Shift: true, Command: "format.numbering"),

        new("=", Ctrl: true, Command: "view.fontBigger"),
        new("-", Ctrl: true, Command: "view.fontSmaller"),
        new("0", Ctrl: true, Command: "view.fontReset"),
        new("r", Ctrl: true, Shift: true, Command: "view.readOnly"),
        new("d", Ctrl: true, Shift: true, Command: "view.typewriter"),
    ];

    private readonly List<(MenuItem Item, Func<bool> Get, Action<bool> Set)> _checkableItems = [];
    private readonly List<JsonElement> _userMenuSpecs = [];

    private MenuItem _recentMenu = null!;
    private MenuItem _extensionsMenu = null!;
    private MenuItem _lineEndingsMenu = null!;
    private MenuItem _encodingMenu = null!;
    private MenuItem _appearanceMenu = null!;
    private MenuItem _invisiblesMenu = null!;
    private MenuItem _lineHeightMenu = null!;
    private MenuItem _windowMenu = null!;

    private void BuildMenu()
    {
        MainMenu.Items.Clear();

        MainMenu.Items.Add(BuildFileMenu());
        MainMenu.Items.Add(BuildEditMenu());
        MainMenu.Items.Add(BuildFormatMenu());
        MainMenu.Items.Add(BuildViewMenu());
        MainMenu.Items.Add(BuildWindowMenu());
        MainMenu.Items.Add(BuildHelpMenu());

        RegisterInputBindings();
        RefreshMenuChecks();
    }

    // MARK: - Top level menus

    private MenuItem BuildFileMenu()
    {
        var menu = new MenuItem { Header = "_File" };

        menu.Items.Add(Item("_New", "file.new", "Ctrl+N"));
        menu.Items.Add(Item("New _Window", "window.new", "Ctrl+Shift+N"));
        menu.Items.Add(Item("_Open...", "file.open", "Ctrl+O"));

        _recentMenu = new MenuItem { Header = "Open _Recent" };
        _recentMenu.SubmenuOpened += (_, _) => RebuildRecentMenu();
        menu.Items.Add(_recentMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Save", "file.save", "Ctrl+S"));
        menu.Items.Add(Item("Save _As...", "file.saveAs", "Ctrl+Shift+S"));
        menu.Items.Add(Item("Re_vert to Saved", "file.revert", "Ctrl+R"));

        menu.Items.Add(new Separator());

        _lineEndingsMenu = new MenuItem { Header = "_Line Endings" };
        _lineEndingsMenu.Items.Add(Item("LF (Unix)", "lineEndings.lf"));
        _lineEndingsMenu.Items.Add(Item("CRLF (Windows)", "lineEndings.crlf"));
        _lineEndingsMenu.Items.Add(Item("CR (Classic Mac OS)", "lineEndings.cr"));
        menu.Items.Add(_lineEndingsMenu);

        _encodingMenu = new MenuItem { Header = "Enc_oding" };
        _encodingMenu.Items.Add(Item("UTF-8", "encoding.utf8"));
        _encodingMenu.Items.Add(Item("UTF-8 with BOM", "encoding.utf8bom"));
        _encodingMenu.Items.Add(Item("UTF-16 LE", "encoding.utf16le"));
        _encodingMenu.Items.Add(Item("UTF-16 BE", "encoding.utf16be"));
        _encodingMenu.Items.Add(Item("ANSI (system code page)", "encoding.ansi"));
        menu.Items.Add(_encodingMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Close", "file.close", "Ctrl+W"));
        menu.Items.Add(Item("E_xit", "file.exit", "Alt+F4"));

        return menu;
    }

    private MenuItem BuildEditMenu()
    {
        var menu = new MenuItem { Header = "_Edit" };

        menu.Items.Add(Item("_Undo", "edit.undo", "Ctrl+Z"));
        menu.Items.Add(Item("_Redo", "edit.redo", "Ctrl+Y"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Cu_t", "edit.cut", "Ctrl+X"));
        menu.Items.Add(Item("_Copy", "edit.copy", "Ctrl+C"));
        menu.Items.Add(Item("_Paste", "edit.paste", "Ctrl+V"));
        menu.Items.Add(Item("Select _All", "edit.selectAll", "Ctrl+A"));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Find...", "edit.find", "Ctrl+F"));
        menu.Items.Add(Item("Find _and Replace...", "edit.replace", "Ctrl+H"));
        menu.Items.Add(Item("Find _Next", "edit.findNext", "F3"));
        menu.Items.Add(Item("Find _Previous", "edit.findPrevious", "Shift+F3"));
        menu.Items.Add(Item("Use Selection for Find", "edit.findSelection", "Ctrl+E"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All Occurrences", "edit.selectAllOccurrences", "Ctrl+Shift+E"));
        menu.Items.Add(Item("Select Next Occurrence", "edit.selectNextOccurrence", "Ctrl+D"));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Go to Line...", "edit.gotoLine", "Ctrl+G"));
        menu.Items.Add(Item("Table of _Contents", "edit.toc", "Ctrl+T"));

        return menu;
    }

    private MenuItem BuildFormatMenu()
    {
        var menu = new MenuItem { Header = "F_ormat" };

        menu.Items.Add(Item("_Bold", "format.bold", "Ctrl+B"));
        menu.Items.Add(Item("_Italic", "format.italic", "Ctrl+I"));
        menu.Items.Add(Item("_Strikethrough", "format.strikethrough", "Ctrl+Shift+X"));
        menu.Items.Add(new Separator());

        for (var level = 1; level <= 6; level++)
        {
            menu.Items.Add(Item($"Heading {level}", $"format.heading{level}", $"Ctrl+{level}"));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Bullet List", "format.bullet", "Ctrl+Shift+L"));
        menu.Items.Add(Item("_Numbered List", "format.numbering", "Ctrl+Shift+O"));
        menu.Items.Add(Item("_Todo", "format.todo"));
        menu.Items.Add(Item("Bloc_kquote", "format.blockquote"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Inline _Code", "format.inlineCode", "Ctrl+`"));
        menu.Items.Add(Item("Inline _Math", "format.inlineMath"));
        menu.Items.Add(Item("Code _Block", "format.codeBlock"));
        menu.Items.Add(Item("Math Bloc_k", "format.mathBlock"));
        menu.Items.Add(Item("_Horizontal Rule", "format.horizontalRule"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Insert _Link...", "format.link", "Ctrl+K"));
        menu.Items.Add(Item("Insert _Table", "format.table"));
        menu.Items.Add(new Separator());

        var editCommands = new MenuItem { Header = "_Edit Commands" };
        editCommands.Items.Add(Item("Indent Less", "format.indentLess", "Ctrl+["));
        editCommands.Items.Add(Item("Indent More", "format.indentMore", "Ctrl+]"));
        editCommands.Items.Add(new Separator());
        editCommands.Items.Add(Item("Expand Selection", "format.expandSelection", "Shift+Alt+Right"));
        editCommands.Items.Add(Item("Shrink Selection", "format.shrinkSelection", "Shift+Alt+Left"));
        editCommands.Items.Add(Item("Select Line", "format.selectLine", "Ctrl+L"));
        editCommands.Items.Add(new Separator());
        editCommands.Items.Add(Item("Move Line Up", "format.moveLineUp", "Alt+Up"));
        editCommands.Items.Add(Item("Move Line Down", "format.moveLineDown", "Alt+Down"));
        editCommands.Items.Add(Item("Copy Line Up", "format.copyLineUp", "Shift+Alt+Up"));
        editCommands.Items.Add(Item("Copy Line Down", "format.copyLineDown", "Shift+Alt+Down"));
        editCommands.Items.Add(new Separator());
        editCommands.Items.Add(Item("Toggle Line Comment", "format.toggleLineComment", "Ctrl+/"));
        editCommands.Items.Add(Item("Toggle Block Comment", "format.toggleBlockComment", "Ctrl+Shift+A"));
        menu.Items.Add(editCommands);

        return menu;
    }

    private MenuItem BuildViewMenu()
    {
        var menu = new MenuItem { Header = "_View" };

        menu.Items.Add(Checkable("Show Line _Numbers", () => _settings.ShowLineNumbers, value =>
        {
            _settings.ShowLineNumbers = value;
            _settings.Save();
            PostWeb("config.setShowLineNumbers", new { enabled = value });
        }));

        menu.Items.Add(Checkable("Show _Active Line Indicator", () => _settings.ShowActiveLineIndicator, value =>
        {
            _settings.ShowActiveLineIndicator = value;
            _settings.Save();
            PostWeb("config.setShowActiveLineIndicator", new { enabled = value });
        }));

        _invisiblesMenu = new MenuItem { Header = "Show _Invisibles" };
        foreach (var behavior in new[] { "never", "selection", "trailing", "always" })
        {
            var captured = behavior;
            _invisiblesMenu.Items.Add(Item(TitleCase(behavior), $"view.invisibles.{behavior}", isChecked: () => _settings.InvisiblesBehavior == captured));
        }

        menu.Items.Add(_invisiblesMenu);

        menu.Items.Add(new Separator());

        menu.Items.Add(Checkable("Line _Wrapping", () => _settings.LineWrapping, value =>
        {
            _settings.LineWrapping = value;
            _settings.Save();
            PostWeb("config.setLineWrapping", new { enabled = value });
        }));

        _lineHeightMenu = new MenuItem { Header = "Line _Height" };
        foreach (var height in new[] { 1.2, 1.5, 1.8 })
        {
            var captured = height;
            _lineHeightMenu.Items.Add(Item(height.ToString("0.0"), $"view.lineHeight.{height.ToString("0.0").Replace('.', '_')}", isChecked: () => Math.Abs(_settings.LineHeight - captured) < 0.01));
        }

        menu.Items.Add(_lineHeightMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(Checkable("_Typewriter Mode", () => _settings.TypewriterMode, value =>
        {
            _settings.TypewriterMode = value;
            _settings.Save();
            PostWeb("config.setTypewriterMode", new { enabled = value });
        }));

        menu.Items.Add(Checkable("_Focus Mode", () => _settings.FocusMode, value =>
        {
            _settings.FocusMode = value;
            _settings.Save();
            PostWeb("config.setFocusMode", new { enabled = value });
        }));

        menu.Items.Add(Checkable("_Read Only Mode", () => IsReadOnly, SetReadOnly));

        menu.Items.Add(new Separator());

        _appearanceMenu = new MenuItem { Header = "_Appearance" };
        _appearanceMenu.SubmenuOpened += (_, _) => RebuildAppearanceMenu();
        menu.Items.Add(_appearanceMenu);

        var fontSize = new MenuItem { Header = "_Font Size" };
        fontSize.Items.Add(Item("Bigger", "view.fontBigger", "Ctrl+="));
        fontSize.Items.Add(Item("Smaller", "view.fontSmaller", "Ctrl+-"));
        fontSize.Items.Add(Item("Reset to Default", "view.fontReset", "Ctrl+0"));
        menu.Items.Add(fontSize);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_Reload", "view.reload"));

        return menu;
    }

    private MenuItem BuildWindowMenu()
    {
        _windowMenu = new MenuItem { Header = "_Window" };
        _windowMenu.SubmenuOpened += (_, _) => RebuildWindowMenu();
        return _windowMenu;
    }

    private MenuItem BuildHelpMenu()
    {
        var menu = new MenuItem { Header = "_Help" };
        menu.Items.Add(Item("MarkEdit on _GitHub", "help.github"));
        menu.Items.Add(Item("_Settings Folder", "help.settingsFolder"));
        menu.Items.Add(Item("_Customization Guide", "help.customization"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("_About MarkEdit", "help.about"));
        return menu;
    }

    // MARK: - Item helpers

    private MenuItem Item(string header, string command, string? gesture = null, Func<bool>? isChecked = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture is not null)
        {
            item.InputGestureText = gesture;
        }

        item.Click += (_, _) => ExecuteCommand(command);

        if (isChecked is not null)
        {
            item.IsCheckable = true;
            _checkableItems.Add((item, isChecked, _ => { }));
        }

        return item;
    }

    private MenuItem Checkable(string header, Func<bool> get, Action<bool> set, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        if (gesture is not null)
        {
            item.InputGestureText = gesture;
        }

        item.Click += (_, _) =>
        {
            set(item.IsChecked);
            RefreshMenuChecks();
        };

        _checkableItems.Add((item, get, set));
        return item;
    }

    private void RefreshMenuChecks()
    {
        foreach (var (item, get, _) in _checkableItems)
        {
            item.IsChecked = get();
        }
    }

    private static string TitleCase(string value) => value.Length == 0
        ? value
        : char.ToUpperInvariant(value[0]) + value[1..];

    private void RegisterInputBindings()
    {
        InputBindings.Clear();

        foreach (var shortcut in Shortcuts)
        {
            var modifiers = ModifierKeys.None;
            if (shortcut.Ctrl)
            {
                modifiers |= ModifierKeys.Control;
            }

            if (shortcut.Shift)
            {
                modifiers |= ModifierKeys.Shift;
            }

            if (shortcut.Alt)
            {
                modifiers |= ModifierKeys.Alt;
            }

            var key = ParseKey(shortcut.Key);
            if (key is null)
            {
                continue;
            }

            var command = shortcut.Command;
            InputBindings.Add(new KeyBinding(new RelayCommand(() => ExecuteCommand(command)), key.Value, modifiers));
        }
    }

    private static Key? ParseKey(string value)
    {
        switch (value)
        {
            case "0": return Key.D0;
            case "1": return Key.D1;
            case "2": return Key.D2;
            case "3": return Key.D3;
            case "4": return Key.D4;
            case "5": return Key.D5;
            case "6": return Key.D6;
            case "7": return Key.D7;
            case "8": return Key.D8;
            case "9": return Key.D9;
            case "/": return Key.OemQuestion;
            case "[": return Key.OemOpenBrackets;
            case "]": return Key.OemCloseBrackets;
            case "=": return Key.OemPlus;
            case "-": return Key.OemMinus;
            case "`": return Key.OemTilde;
        }

        // Letters and function keys share their names with the WPF Key enum values.
        return Enum.TryParse<Key>(value, ignoreCase: true, out var key) ? key : null;
    }

    private static string ShortcutScript()
    {
        var table = Shortcuts.Select(shortcut => new
        {
            key = shortcut.Key,
            ctrl = shortcut.Ctrl,
            shift = shortcut.Shift,
            alt = shortcut.Alt,
            command = shortcut.Command,
        });

        return $"window.__markeditInstallShortcuts({JsonSerializer.Serialize(table)});";
    }

    // MARK: - Dynamic submenus

    private void RebuildRecentMenu()
    {
        _recentMenu.Items.Clear();

        var files = _settings.RecentFiles;
        if (files.Length == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "No Recent Files", IsEnabled = false });
            return;
        }

        foreach (var file in files)
        {
            var item = new MenuItem { Header = file };
            var captured = file;
            item.Click += (_, _) => OpenDocument(captured, reuseEmptyWindow: true);
            _recentMenu.Items.Add(item);
        }

        _recentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Menu" };
        clear.Click += (_, _) =>
        {
            _settings.RecentFiles = [];
            _settings.Save();
        };

        _recentMenu.Items.Add(clear);
    }

    private void RebuildAppearanceMenu()
    {
        _appearanceMenu.Items.Clear();

        var current = _settings.Theme;
        _appearanceMenu.Items.Add(Item("System", "view.theme.auto", isChecked: () => string.Equals(current, "auto", StringComparison.OrdinalIgnoreCase)));
        _appearanceMenu.Items.Add(new Separator());

        var themes = new (string Name, string Title)[]
        {
            ("github-light", "GitHub Light"),
            ("github-dark", "GitHub Dark"),
            ("xcode-light", "Xcode Light"),
            ("xcode-dark", "Xcode Dark"),
            ("minimal-light", "Minimal Light"),
            ("minimal-dark", "Minimal Dark"),
            ("solarized-light", "Solarized Light"),
            ("solarized-dark", "Solarized Dark"),
            ("dracula", "Dracula"),
            ("cobalt", "Cobalt"),
            ("night-owl", "Night Owl"),
            ("rose-pine", "Rosé Pine"),
            ("rose-pine-dawn", "Rosé Pine Dawn"),
            ("synthwave84", "SynthWave '84"),
            ("winter-is-coming-light", "Winter is Coming Light"),
            ("winter-is-coming-dark", "Winter is Coming Dark"),
        };

        foreach (var (name, title) in themes)
        {
            var captured = name;
            _appearanceMenu.Items.Add(Item(title, $"view.theme.{name}", isChecked: () => string.Equals(current, captured, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void RebuildWindowMenu()
    {
        _windowMenu.Items.Clear();
        _windowMenu.Items.Add(Item("_New Window", "window.new", "Ctrl+Shift+N"));
        _windowMenu.Items.Add(new Separator());
        _windowMenu.Items.Add(Item("_Minimize", "window.minimize"));
        _windowMenu.Items.Add(Item("Ma_ximize", "window.maximize"));
        _windowMenu.Items.Add(new Separator());

        var windows = App.CurrentApp?.OpenWindows ?? [];
        foreach (var window in windows)
        {
            var item = new MenuItem
            {
                Header = window.Title.Replace("_", "__", StringComparison.Ordinal),
                IsCheckable = true,
                IsChecked = ReferenceEquals(window, this),
            };

            var captured = window;
            item.Click += (_, _) =>
            {
                captured.Activate();
                if (captured.WindowState == WindowState.Minimized)
                {
                    captured.WindowState = WindowState.Normal;
                }
            };

            _windowMenu.Items.Add(item);
        }
    }

    // MARK: - User defined menu items

    public void AddUserMenuItems(JsonElement items)
    {
        _userMenuSpecs.Clear();
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                _userMenuSpecs.Add(item.Clone());
            }
        }

        RebuildExtensionsMenu();
    }

    private void RebuildExtensionsMenu()
    {
        var index = MainMenu.Items.IndexOf(_extensionsMenu);
        var menu = BuildExtensionsMenu();

        if (index >= 0)
        {
            MainMenu.Items.RemoveAt(index);
            MainMenu.Items.Insert(index, menu);
        }
        else
        {
            MainMenu.Items.Insert(Math.Max(0, MainMenu.Items.Count - 1), menu);
        }
    }

    private MenuItem BuildExtensionsMenu()
    {
        _extensionsMenu = new MenuItem { Header = "E_xtensions" };
        _extensionsMenu.SubmenuOpened += async (_, _) => await RefreshUserMenuStatesAsync();

        if (_userMenuSpecs.Count == 0)
        {
            _extensionsMenu.Items.Add(new MenuItem { Header = "No Extensions", IsEnabled = false });
        }
        else
        {
            foreach (var spec in _userMenuSpecs)
            {
                _extensionsMenu.Items.Add(BuildUserMenuItem(spec));
            }
        }

        _extensionsMenu.Items.Add(new Separator());
        _extensionsMenu.Items.Add(Item("Open _Settings Folder", "help.settingsFolder"));
        return _extensionsMenu;
    }

    private MenuItem BuildUserMenuItem(JsonElement spec)
    {
        var separator = spec.TryGetProperty("separator", out var separatorElement) && separatorElement.ValueKind == JsonValueKind.True;
        if (separator)
        {
            return new MenuItem { Header = "-" };
        }

        var title = spec.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
        var item = new MenuItem { Header = title.Replace("_", "__", StringComparison.Ordinal) };

        var actionId = spec.TryGetProperty("actionID", out var actionElement) ? actionElement.GetString() : null;
        var stateId = spec.TryGetProperty("stateGetterID", out var stateElement) ? stateElement.GetString() : null;

        if (stateId is not null)
        {
            item.Tag = stateId;
        }

        if (actionId is not null)
        {
            item.Click += async (_, _) => await InvokeWebAsync("api.handleMainMenuAction", new { id = actionId });
        }

        if (spec.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                item.Items.Add(BuildUserMenuItem(child));
            }
        }

        return item;
    }

    private async Task RefreshUserMenuStatesAsync()
    {
        foreach (var item in EnumerateMenuItems(_extensionsMenu))
        {
            if (item.Tag is not string stateId)
            {
                continue;
            }

            var state = await InvokeWebAsync("api.getMenuItemState", new { id = stateId });
            if (state is not { ValueKind: JsonValueKind.Object })
            {
                continue;
            }

            if (state.Value.TryGetProperty("isEnabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                item.IsEnabled = enabled.GetBoolean();
            }

            if (state.Value.TryGetProperty("isSelected", out var selected) && selected.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                item.IsCheckable = true;
                item.IsChecked = selected.GetBoolean();
            }
        }
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl parent)
    {
        foreach (var entry in parent.Items)
        {
            if (entry is not MenuItem item)
            {
                continue;
            }

            yield return item;

            foreach (var child in EnumerateMenuItems(item))
            {
                yield return child;
            }
        }
    }

    // MARK: - Context menus

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;

        var menu = BuildEditorContextMenu();
        menu.PlacementTarget = WebView;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = e.Location.X;
        menu.VerticalOffset = e.Location.Y;
        menu.IsOpen = true;
    }

    private ContextMenu BuildEditorContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item("Undo", "edit.undo"));
        menu.Items.Add(Item("Redo", "edit.redo"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Cut", "edit.cut"));
        menu.Items.Add(Item("Copy", "edit.copy"));
        menu.Items.Add(Item("Paste", "edit.paste"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All", "edit.selectAll"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Bold", "format.bold"));
        menu.Items.Add(Item("Italic", "format.italic"));
        menu.Items.Add(Item("Insert Link...", "format.link"));

        return menu;
    }

    public void ShowContextMenu(JsonElement items, double x, double y)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var spec in items.EnumerateArray())
        {
            menu.Items.Add(BuildUserMenuItem(spec));
        }

        menu.PlacementTarget = WebView;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        menu.HorizontalOffset = x;
        menu.VerticalOffset = y;
        menu.IsOpen = true;
    }

    private sealed record Shortcut(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false, string Command = "");
}

/// <summary>Minimal ICommand implementation for keyboard bindings.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute;

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
