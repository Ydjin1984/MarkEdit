using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarkEditWin.Services;
using MarkEditWin.Views;

namespace MarkEditWin;

/// <summary>
/// Command dispatch shared by menu items, keyboard shortcuts and the page level shortcut table.
/// </summary>
public partial class MainWindow
{
    private void ExecuteCommand(string command)
    {
        _ = ExecuteCommandAsync(command);
    }

    private async Task ExecuteCommandAsync(string command)
    {
        try
        {
            await ExecuteCommandCoreAsync(command);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExecuteCommandCoreAsync(string command)
    {
        if (command.StartsWith("format.", StringComparison.Ordinal))
        {
            await ExecuteFormatCommandAsync(command);
            return;
        }

        if (command.StartsWith("view.theme.", StringComparison.Ordinal))
        {
            _settings.Theme = command["view.theme.".Length..];
            _settings.Save();
            ApplyTheme(NativeTheme.ResolveTheme(_settings.Theme));
            return;
        }

        if (command.StartsWith("view.invisibles.", StringComparison.Ordinal))
        {
            _settings.InvisiblesBehavior = command["view.invisibles.".Length..];
            _settings.Save();
            PostWeb("config.setInvisiblesBehavior", new { behavior = _settings.InvisiblesBehavior });
            RefreshMenuChecks();
            return;
        }

        if (command.StartsWith("view.lineHeight.", StringComparison.Ordinal))
        {
            var value = command["view.lineHeight.".Length..].Replace('_', '.');
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lineHeight))
            {
                _settings.LineHeight = lineHeight;
                _settings.Save();
                PostWeb("config.setLineHeight", new { lineHeight });
                RefreshMenuChecks();
            }

            return;
        }

        if (command.StartsWith("lineEndings.", StringComparison.Ordinal))
        {
            SetLineEnding(command["lineEndings.".Length..]);
            return;
        }

        if (command.StartsWith("encoding.", StringComparison.Ordinal))
        {
            SetEncoding(command["encoding.".Length..]);
            return;
        }

        switch (command)
        {
            case "file.new":
            case "window.new":
                App.CurrentApp?.CreateWindow(null);
                break;

            case "file.open":
                OpenDocumentFromDialog();
                break;

            case "file.save":
                await SaveDocumentAsync();
                break;

            case "file.saveAs":
                await SaveDocumentAsAsync();
                break;

            case "file.revert":
                await RevertDocumentAsync();
                break;

            case "file.close":
                Close();
                break;

            case "file.exit":
                Application.Current.Shutdown();
                break;

            case "edit.undo":
                await RunWebCommandAsync("history.undo");
                break;

            case "edit.redo":
                await RunWebCommandAsync("history.redo");
                break;

            case "edit.cut":
                await CutAsync();
                break;

            case "edit.copy":
                await CopyAsync();
                break;

            case "edit.paste":
                await PasteAsync();
                break;

            case "edit.selectAll":
                await RunWebCommandAsync("selection.selectWholeDocument");
                break;

            case "edit.find":
                ShowFindBar(replace: false);
                break;

            case "edit.replace":
                ShowFindBar(replace: true);
                break;

            case "edit.findNext":
                await FindNextAsync(backwards: false);
                break;

            case "edit.findPrevious":
                await FindNextAsync(backwards: true);
                break;

            case "edit.findSelection":
                await UseSelectionForFindAsync();
                break;

            case "edit.selectAllOccurrences":
                await RunWebCommandAsync("search.selectAllOccurrences");
                break;

            case "edit.selectNextOccurrence":
                await RunWebCommandAsync("search.selectNextOccurrence");
                break;

            case "edit.gotoLine":
                await GoToLineAsync();
                break;

            case "edit.toc":
                await ShowTableOfContentsAsync();
                break;

            case "view.fontBigger":
                SetFontSize(_settings.FontSize + 1);
                break;

            case "view.fontSmaller":
                SetFontSize(_settings.FontSize - 1);
                break;

            case "view.fontReset":
                SetFontSize(17);
                break;

            case "view.readOnly":
                SetReadOnly(!IsReadOnly);
                break;

            case "view.typewriter":
                _settings.TypewriterMode = !_settings.TypewriterMode;
                _settings.Save();
                PostWeb("config.setTypewriterMode", new { enabled = _settings.TypewriterMode });
                RefreshMenuChecks();
                break;

            case "view.reload":
                ReloadEditor();
                break;

            case "findBar.close":
                HideFindBar();
                break;

            case "window.minimize":
                WindowState = WindowState.Minimized;
                break;

            case "window.maximize":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;

            case "help.github":
                ShellLauncher.TryOpen("https://github.com/MarkEdit-app/MarkEdit");
                break;

            case "help.settingsFolder":
                AppPaths.EnsureCreated();
                ShellLauncher.TryOpen(AppPaths.UserDataDirectory);
                break;

            case "help.customization":
                ShellLauncher.TryOpen("https://github.com/MarkEdit-app/MarkEdit/wiki/Customization");
                break;

            case "help.about":
                MessageBox.Show(
                    this,
                    $"MarkEdit for Windows\n\nVersion {AppVersion} (build {AppBuild})\nA Markdown editor built on the MarkEdit editor core.\n\nLicensed under the MIT License.",
                    "About MarkEdit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                break;

            default:
                break;
        }
    }

    // MARK: - Format commands

    private async Task ExecuteFormatCommandAsync(string command)
    {
        switch (command)
        {
            case "format.bold": await RunWebCommandAsync("format.toggleBold"); break;
            case "format.italic": await RunWebCommandAsync("format.toggleItalic"); break;
            case "format.strikethrough": await RunWebCommandAsync("format.toggleStrikethrough"); break;
            case "format.heading1": await RunWebCommandAsync("format.toggleHeading", new { level = 1 }); break;
            case "format.heading2": await RunWebCommandAsync("format.toggleHeading", new { level = 2 }); break;
            case "format.heading3": await RunWebCommandAsync("format.toggleHeading", new { level = 3 }); break;
            case "format.heading4": await RunWebCommandAsync("format.toggleHeading", new { level = 4 }); break;
            case "format.heading5": await RunWebCommandAsync("format.toggleHeading", new { level = 5 }); break;
            case "format.heading6": await RunWebCommandAsync("format.toggleHeading", new { level = 6 }); break;
            case "format.bullet": await RunWebCommandAsync("format.toggleBullet"); break;
            case "format.numbering": await RunWebCommandAsync("format.toggleNumbering"); break;
            case "format.todo": await RunWebCommandAsync("format.toggleTodo"); break;
            case "format.blockquote": await RunWebCommandAsync("format.toggleBlockquote"); break;
            case "format.inlineCode": await RunWebCommandAsync("format.toggleInlineCode"); break;
            case "format.inlineMath": await RunWebCommandAsync("format.toggleInlineMath"); break;
            case "format.codeBlock": await RunWebCommandAsync("format.insertCodeBlock"); break;
            case "format.mathBlock": await RunWebCommandAsync("format.insertMathBlock"); break;
            case "format.horizontalRule": await RunWebCommandAsync("format.insertHorizontalRule"); break;
            case "format.link": await InsertLinkAsync(); break;
            case "format.table": await InsertTableAsync(); break;

            case "format.indentLess": await RunEditCommandAsync("indentLess"); break;
            case "format.indentMore": await RunEditCommandAsync("indentMore"); break;
            case "format.expandSelection": await RunEditCommandAsync("expandSelection"); break;
            case "format.shrinkSelection": await RunEditCommandAsync("shrinkSelection"); break;
            case "format.selectLine": await RunEditCommandAsync("selectLine"); break;
            case "format.moveLineUp": await RunEditCommandAsync("moveLineUp"); break;
            case "format.moveLineDown": await RunEditCommandAsync("moveLineDown"); break;
            case "format.copyLineUp": await RunEditCommandAsync("copyLineUp"); break;
            case "format.copyLineDown": await RunEditCommandAsync("copyLineDown"); break;
            case "format.toggleLineComment": await RunEditCommandAsync("toggleLineComment"); break;
            case "format.toggleBlockComment": await RunEditCommandAsync("toggleBlockComment"); break;
        }
    }

    private Task RunEditCommandAsync(string command)
    {
        return RunWebCommandAsync("format.performEditCommand", new { command });
    }

    private async Task InsertLinkAsync()
    {
        var title = TextInputDialog.Show(this, "Insert Link", "Title", "title");
        if (title is null)
        {
            return;
        }

        var url = TextInputDialog.Show(this, "Insert Link", "https://example.com", null);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await RunWebCommandAsync("format.insertHyperLink", new { title, url, prefix = (string?)null });
    }

    private async Task InsertTableAsync()
    {
        var columnName = TextInputDialog.Show(this, "Insert Table", "Column name", "Column");
        if (columnName is null)
        {
            return;
        }

        var itemName = TextInputDialog.Show(this, "Insert Table", "Item name", "Item");
        if (itemName is null)
        {
            return;
        }

        await RunWebCommandAsync("format.insertTable", new { columnName, itemName });
    }

    /// <summary>Invokes a web module function and returns focus to the editor.</summary>
    private async Task RunWebCommandAsync(string path, object? message = null)
    {
        WebView.Focus();
        await InvokeWebAsync(path, message);
    }

    // MARK: - Clipboard

    private async Task CopyAsync()
    {
        var text = await SelectedTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            SetClipboardText(text);
        }
    }

    private async Task CutAsync()
    {
        var text = await SelectedTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SetClipboardText(text);
        await RunWebCommandAsync("core.replaceText", new { text = string.Empty, granularity = "selection" });
    }

    private async Task PasteAsync()
    {
        var text = GetClipboardText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await RunWebCommandAsync("core.replaceText", new { text, granularity = "selection" });
    }

    private async Task<string?> SelectedTextAsync()
    {
        var result = await InvokeWebAsync("selection.getText");
        return result is { ValueKind: System.Text.Json.JsonValueKind.String } ? result.Value.GetString() : null;
    }

    private void SetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process.
        }
    }

    private static string? GetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // MARK: - Go to line and table of contents

    private async Task GoToLineAsync()
    {
        var input = TextInputDialog.Show(this, "Go to Line", "Line number", null);
        if (input is null || !int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber))
        {
            return;
        }

        var text = await GetEditorTextAsync() ?? string.Empty;
        var lineCount = Math.Max(1, text.Split('\n').Length);
        lineNumber = Math.Clamp(lineNumber, 1, lineCount);

        await RunWebCommandAsync("selection.gotoLine", new { lineNumber });
    }

    private async Task ShowTableOfContentsAsync()
    {
        var result = await InvokeWebAsync("toc.getTableOfContents");
        if (result is not { ValueKind: System.Text.Json.JsonValueKind.Array } headings || headings.GetArrayLength() == 0)
        {
            MessageBox.Show(this, "This document has no headings.", "Table of Contents", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var heading in headings.EnumerateArray())
        {
            var title = heading.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
            var level = heading.TryGetProperty("level", out var levelElement) ? levelElement.GetInt32() : 1;

            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"{new string(' ', Math.Max(0, level - 1) * 4)}{title}",
                FontWeight = level == 1 ? FontWeights.SemiBold : FontWeights.Normal,
            };

            var captured = heading.Clone();
            item.Click += async (_, _) => await RunWebCommandAsync("toc.gotoHeader", new { headingInfo = captured });
            menu.Items.Add(item);
        }

        menu.PlacementTarget = WebView;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // MARK: - Line endings and encoding

    private void SetLineEnding(string name)
    {
        _document.LineEnding = name switch
        {
            "crlf" => LineEnding.CRLF,
            "cr" => LineEnding.CR,
            _ => LineEnding.LF,
        };

        _document.IsDirty = true;
        PostWeb("lineEndings.setLineEndings", new { lineEndings = LineEndingsValue });
        PostWeb("config.setDefaultLineBreak", new { lineBreak = LineBreakCharacters });
        UpdateStatusBar();
        UpdateTitle();
    }

    private void SetEncoding(string name)
    {
        _document.Encoding = name switch
        {
            "utf8bom" => new System.Text.UTF8Encoding(true),
            "utf16le" => new System.Text.UnicodeEncoding(false, true),
            "utf16be" => new System.Text.UnicodeEncoding(true, true),
            "ansi" => System.Text.Encoding.GetEncoding(0),
            _ => new System.Text.UTF8Encoding(false),
        };

        _document.IsDirty = true;
        UpdateStatusBar();
        UpdateTitle();
    }

    private void ReloadEditor()
    {
        if (_document.IsDirty)
        {
            var answer = MessageBox.Show(
                this,
                "Reloading reloads the editor and the document from disk. Unsaved changes are discarded.\n\nContinue?",
                "MarkEdit",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        _core?.Reload();
    }
}
