using System.ComponentModel;
using System.IO;
using System.Windows;
using MarkEditWin.Services;
using MarkEditWin.Views;
using Microsoft.Win32;

namespace MarkEditWin;

/// <summary>
/// Opening, saving, reverting and closing documents, plus the find bar wiring.
/// </summary>
public partial class MainWindow
{
    private const string FileFilter =
        "Markdown Files (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|All Files (*.*)|*.*";

    // MARK: - Opening

    private void OpenDocumentFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open",
            InitialDirectory = _settings.DefaultOpenDirectory,
            Filter = FileFilter,
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var files = dialog.FileNames;
        for (var index = 0; index < files.Length; index++)
        {
            OpenDocument(files[index], reuseEmptyWindow: index == 0);
        }

        WebView.Focus();
    }

    private void OpenDocument(string path, bool reuseEmptyWindow)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"The file could not be found:\n\n{path}", "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
            ForgetRecentFile(path);
            return;
        }

        var canReuse = reuseEmptyWindow && _document.FilePath is null && !_document.HasBeenEdited && !_document.IsDirty;
        if (canReuse)
        {
            _ = LoadDocumentAsync(path);
            return;
        }

        App.CurrentApp?.CreateWindow(path);
    }

    private async Task LoadDocumentAsync(string path)
    {
        try
        {
            _document.LoadFrom(path);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Unable to read the file:\n\n{exception.Message}", "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AddRecentFile(path);
        await PushDocumentAsync();
    }

    // MARK: - Saving

    public async Task<bool> SaveDocumentAsync()
    {
        if (_document.FilePath is null)
        {
            return await SaveDocumentAsAsync();
        }

        return await WriteDocumentAsync(_document.FilePath);
    }

    public async Task<bool> SaveDocumentAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save As",
            InitialDirectory = _document.DirectoryPath ?? _settings.DefaultSaveDirectory,
            FileName = _document.FilePath is null ? "Untitled.md" : Path.GetFileName(_document.FilePath),
            Filter = FileFilter,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        var path = dialog.FileName;
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            path += ".md";
        }

        return await WriteDocumentAsync(path);
    }

    private async Task<bool> WriteDocumentAsync(string path)    {
        var text = await GetEditorTextAsync();
        if (text is null)
        {
            return false;
        }

        try
        {
            _document.FilePath = path;
            _document.WriteText(text);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Unable to save the file:\n\n{exception.Message}", "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        await InvokeWebAsync("history.markContentClean");
        _document.HasBeenEdited = false;
        _document.IsDirty = false;

        AddRecentFile(path);
        UpdateTitle();
        UpdateStatusBar();
        return true;
    }

    /// <summary>Saves the editor content to an explicit path, bypassing the save dialog.</summary>
    public Task<bool> SaveToPathAsync(string path)
    {
        return WriteDocumentAsync(path);
    }

    private async Task RevertDocumentAsync()
    {
        if (_document.FilePath is null)
        {
            return;
        }

        if (_document.IsDirty)
        {
            var answer = MessageBox.Show(
                this,
                "Reverting discards unsaved changes. Continue?",
                "MarkEdit",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        await LoadDocumentAsync(_document.FilePath);
    }

    // MARK: - Closing

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        if (!_document.IsDirty && !(_document.HasBeenEdited && _document.FilePath is null))
        {
            base.OnClosing(e);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Do you want to save the changes you made to \u201c{_document.DisplayName}\u201d?",
            "MarkEdit",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                return;

            case MessageBoxResult.No:
                base.OnClosing(e);
                return;

            default:
                e.Cancel = true;
                _ = SaveThenCloseAsync();
                return;
        }
    }

    private async Task SaveThenCloseAsync()
    {
        if (!await SaveDocumentAsync())
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    // MARK: - Recent files

    private void AddRecentFile(string path)
    {
        var files = _settings.RecentFiles
            .Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        files.Insert(0, path);
        _settings.RecentFiles = files.Take(15).ToArray();
        _settings.Save();
    }

    private void ForgetRecentFile(string path)
    {
        _settings.RecentFiles = _settings.RecentFiles
            .Where(file => !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _settings.Save();
    }

    // MARK: - Find bar

    private void WireFindBar()
    {
        FindBarControl.SearchTermChanged += async (_, options) => await UpdateSearchQueryAsync(options, refocus: false);
        FindBarControl.OptionsChanged += async (_, options) => await UpdateSearchQueryAsync(options, refocus: true);
        FindBarControl.NextRequested += async (_, _) => await FindNextAsync(backwards: false);
        FindBarControl.PreviousRequested += async (_, _) => await FindNextAsync(backwards: true);
        FindBarControl.ReplaceRequested += async (_, _) => await ReplaceNextAsync();
        FindBarControl.ReplaceAllRequested += async (_, _) => await ReplaceAllAsync();
        FindBarControl.CloseRequested += (_, _) => HideFindBar();
    }

    private void ShowFindBar(bool replace)
    {
        FindBarControl.Visibility = Visibility.Visible;
        FindBarControl.IsReplaceVisible = replace;
        SetFindBarOpen(true);

        if (string.IsNullOrEmpty(FindBarControl.SearchTerm))
        {
            _ = PrefillSearchFromSelectionAsync();
        }
        else
        {
            _ = UpdateSearchQueryAsync(FindBarControl.Options, refocus: false);
        }

        if (replace)
        {
            FindBarControl.FocusReplaceField();
        }
        else
        {
            FindBarControl.FocusSearchField(selectAll: true);
        }
    }

    private async Task PrefillSearchFromSelectionAsync()
    {
        var selected = await SelectedTextAsync();
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
        {
            FindBarControl.SearchTerm = selected;
        }

        await UpdateSearchQueryAsync(FindBarControl.Options, refocus: false);
    }

    private void HideFindBar()
    {
        FindBarControl.Visibility = Visibility.Collapsed;
        FindBarControl.IsReplaceVisible = false;
        FindBarControl.ClearCounter();
        SetFindBarOpen(false);
        PostWeb("search.setState", new { enabled = false });
        WebView.Focus();
    }

    private async Task UseSelectionForFindAsync()
    {
        var selected = await SelectedTextAsync();
        if (!string.IsNullOrEmpty(selected))
        {
            FindBarControl.SearchTerm = selected;
        }

        FindBarControl.Visibility = Visibility.Visible;
        SetFindBarOpen(true);
        FindBarControl.FocusSearchField(selectAll: true);
        await UpdateSearchQueryAsync(FindBarControl.Options, refocus: true);
    }

    private async Task UpdateSearchQueryAsync(FindOptions options, bool refocus)
    {
        if (FindBarControl.Visibility != Visibility.Visible)
        {
            return;
        }

        await InvokeWebAsync("search.updateQuery", new
        {
            options = new
            {
                search = options.Search,
                caseSensitive = options.CaseSensitive,
                diacriticInsensitive = options.DiacriticInsensitive,
                wholeWord = options.WholeWord,
                literal = options.Literal,
                regexp = options.RegularExpression,
                refocus,
                replace = options.Replace,
            },
        });

        await UpdateSearchCounterAsync();
    }

    private async Task UpdateSearchCounterAsync()
    {
        var result = await InvokeWebAsync("search.getCounterInfo");
        var count = 0;
        var index = -1;

        if (result is { ValueKind: System.Text.Json.JsonValueKind.Object } counter)
        {
            if (counter.TryGetProperty("numberOfItems", out var countElement) && countElement.TryGetInt32(out var parsedCount))
            {
                count = parsedCount;
            }

            if (counter.TryGetProperty("currentIndex", out var indexElement) && indexElement.TryGetInt32(out var parsedIndex))
            {
                index = parsedIndex;
            }
        }

        FindBarControl.UpdateCounter(count, index, string.IsNullOrEmpty(FindBarControl.SearchTerm));
    }

    private async Task FindNextAsync(bool backwards)
    {
        if (FindBarControl.Visibility != Visibility.Visible)
        {
            ShowFindBar(replace: false);
            return;
        }

        var term = FindBarControl.SearchTerm;
        if (string.IsNullOrEmpty(term))
        {
            PlayBeep();
            return;
        }

        await InvokeWebAsync(backwards ? "search.findPrevious" : "search.findNext", new { search = term });
        await UpdateSearchCounterAsync();
    }

    private async Task ReplaceNextAsync()
    {
        await InvokeWebAsync("search.replaceNext");
        await UpdateSearchCounterAsync();
    }

    private async Task ReplaceAllAsync()
    {
        await InvokeWebAsync("search.replaceAll");
        await UpdateSearchCounterAsync();
    }

    private void SetFindBarOpen(bool isOpen)
    {
        _ = ExecuteScriptAsync(isOpen
            ? "window.__markeditSetState && window.__markeditSetState({ findBarOpen: true });"
            : "window.__markeditSetState && window.__markeditSetState({ findBarOpen: false });");
    }

    /// <summary>Runs a script in the page, used by the self-test to inspect the editor state.</summary>
    public async Task<string?> ExecuteScriptForTestAsync(string script)
    {
        if (_core is null)
        {
            return null;
        }

        try
        {
            return await _core.ExecuteScriptAsync(script);
        }
        catch (Exception exception)
        {
            return $"error: {exception.Message}";
        }
    }

    private async Task ExecuteScriptAsync(string script)
    {
        if (_core is null)
        {
            return;
        }

        try
        {
            await _core.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // Script injection is best effort.
        }
    }
}
