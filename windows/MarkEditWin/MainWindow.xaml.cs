using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using MarkEditWin.Bridge;
using MarkEditWin.Bridge.Modules;
using MarkEditWin.Services;
using Microsoft.Web.WebView2.Core;

namespace MarkEditWin;

public partial class MainWindow : Window, IEditorHost
{
    private const string VirtualHost = "markedit.local";

    private readonly AppSettings _settings;
    private readonly DocumentSession _document;
    private readonly NativeModuleDispatcher _dispatcher;

    private CoreWebView2? _core;
    private BridgeHost? _bridge;
    private bool _closeConfirmed;
    private bool _isReadOnly;

    public MainWindow(AppSettings settings, string? filePath)
    {
        _settings = settings;
        _document = filePath is null ? new DocumentSession() : DocumentSession.FromFile(filePath);
        _dispatcher = new NativeModuleDispatcher(
            new CoreModule(this),
            new ApiModule(this),
            new CompletionModule(),
            new TokenizerModule(),
            new FoundationModelsModule(),
            new TranslationModule());

        InitializeComponent();

        BuildMenu();
        WireFindBar();

        NativeTheme.Changed += OnNativeThemeChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public DocumentSession Document => _document;

    public AppSettings Settings => _settings;

    Window? IEditorHost.Window => this;

    public bool IsReadOnly => _isReadOnly;

    /// <summary>Last failure from a web call, used by the self-test to report diagnostics.</summary>
    public string? LastWebError { get; private set; }

    /// <summary>Recent web call failures, kept for diagnostics because later successes clear LastWebError.</summary>
    public List<string> WebErrors { get; } = [];

    public async Task<JsonElement?> InvokeWebAsync(string path, object? message = null)
    {
        if (_bridge is null)
        {
            LastWebError = "bridge is not connected";
            return null;
        }

        try
        {
            var result = await _bridge.InvokeAsync(path, message);
            LastWebError = null;
            return result;
        }
        catch (Exception exception)
        {
            // Most web calls are best effort, e.g. when the window is closing.
            LastWebError = $"{path}: {exception.Message}";
            WebErrors.Add(LastWebError);
            if (WebErrors.Count > 20)
            {
                WebErrors.RemoveAt(0);
            }

            return null;
        }
    }

    public void PostWeb(string path, object? message = null)
    {
        _bridge?.Post(path, message);
    }

    // MARK: - Window lifecycle

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await InitializeWebViewAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        NativeTheme.Changed -= OnNativeThemeChanged;

        if (_bridge is not null)
        {
            _bridge.CommandReceived -= ExecuteCommand;
            _bridge.Dispose();
            _bridge = null;
        }

        _core = null;
        App.CurrentApp?.RemoveWindow(this);
    }

    private void OnNativeThemeChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (string.Equals(_settings.Theme, "auto", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTheme(NativeTheme.ResolveTheme("auto"));
            }
        });
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            AppPaths.EnsureCreated();

            var environment = await App.GetWebViewEnvironmentAsync();
            await WebView.EnsureCoreWebView2Async(environment);
            _core = WebView.CoreWebView2;

            ApplyBrowserSettings(_core);

            _core.ContextMenuRequested += OnContextMenuRequested;
            _core.NavigationStarting += OnNavigationStarting;
            _core.NewWindowRequested += OnNewWindowRequested;
            _core.ProcessFailed += OnProcessFailed;

            _bridge = new BridgeHost(_core, DispatchNativeCallAsync);
            _bridge.CommandReceived += ExecuteCommand;

            await _core.AddScriptToExecuteOnDocumentCreatedAsync(ReadEmbeddedText("MarkEditWin.Assets.bridge-shim.js"));
            await _core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript());

            var htmlFile = await WriteEditorHtmlAsync();
            _core.SetVirtualHostNameToFolderMapping(VirtualHost, AppPaths.CacheDirectory, CoreWebView2HostResourceAccessKind.Allow);
            _core.Navigate($"https://{VirtualHost}/{Path.GetFileName(htmlFile)}");
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Failed to start the editor:\n\n{exception.Message}", "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private static void ApplyBrowserSettings(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreDevToolsEnabled = true;
        settings.IsPinchZoomEnabled = false;
    }

    private static string ReadEmbeddedText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource: {name}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The editor core replaces these placeholders when the page loads. User settings are replaced
    /// first so that the config JSON is not searched for the settings placeholder.
    /// </summary>
    private async Task<string> WriteEditorHtmlAsync()
    {
        var template = ReadEmbeddedText("MarkEditWin.Assets.index.html");
        var html = template
            .Replace("\"{{USER_SETTINGS}}\"", string.IsNullOrWhiteSpace(_settings.RawJson) ? "{}" : _settings.RawJson, StringComparison.Ordinal)
            .Replace("\"{{EDITOR_CONFIG}}\"", BuildEditorConfigJson(), StringComparison.Ordinal);

        html = AppendUserCustomization(html);

        var path = Path.Combine(AppPaths.CacheDirectory, "index.html");
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
        return path;
    }

    private static string AppendUserCustomization(string html)
    {
        var builder = new StringBuilder(html);

        try
        {
            if (File.Exists(AppPaths.CustomCssFile))
            {
                builder.Append("\n<style>\n").Append(File.ReadAllText(AppPaths.CustomCssFile)).Append("\n</style>");
            }

            foreach (var file in EnumerateUserScripts())
            {
                builder.Append("\n<script type=\"module\">\n").Append(File.ReadAllText(file)).Append("\n</script>");
            }
        }
        catch (Exception)
        {
            // Broken user customization must never prevent the editor from loading.
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateUserScripts()
    {
        if (File.Exists(AppPaths.CustomScriptFile))
        {
            yield return AppPaths.CustomScriptFile;
        }

        if (Directory.Exists(AppPaths.ScriptsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.ScriptsDirectory, "*.js").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private string BuildEditorConfigJson()
    {
        var config = new Dictionary<string, object?>
        {
            ["host"] = "mainApp",
            ["text"] = string.Empty,
            ["theme"] = NativeTheme.ResolveTheme(_settings.Theme),
            ["fontFace"] = new Dictionary<string, object?>
            {
                ["family"] = _settings.FontFamily,
            },
            ["fontSize"] = _settings.FontSize,
            ["showLineNumbers"] = _settings.ShowLineNumbers,
            ["showActiveLineIndicator"] = _settings.ShowActiveLineIndicator,
            ["invisiblesBehavior"] = _settings.InvisiblesBehavior,
            ["readOnlyMode"] = false,
            ["typewriterMode"] = _settings.TypewriterMode,
            ["focusMode"] = _settings.FocusMode,
            ["lineWrapping"] = _settings.LineWrapping,
            ["lineHeight"] = _settings.LineHeight,
            ["suggestWhileTyping"] = false,
            ["smartQuotesEnabled"] = false,
            ["tabKeyBehavior"] = 0,
            ["autoCharacterPairs"] = _settings.EditorAutoCharacterPairs,
            ["indentBehavior"] = _settings.EditorIndentBehavior,
            ["defaultLineBreak"] = LineBreakCharacters,
            ["indentUnit"] = "  ",
            ["standardDirectories"] = StandardDirectories(),
            ["runtimeInfo"] = new Dictionary<string, object?>
            {
                ["appVersion"] = AppVersion,
                ["appBuild"] = AppBuild,
                ["osVersion"] = Environment.OSVersion.VersionString,
                ["webkitVersion"] = _core?.Environment.BrowserVersionString ?? string.Empty,
            },
            ["localizable"] = new Dictionary<string, object?>
            {
                ["controlCharacter"] = "Control Character",
                ["foldedLines"] = "Folded Lines",
                ["unfoldedLines"] = "Unfolded Lines",
                ["foldedCode"] = "Folded Code",
                ["unfold"] = "Unfold",
                ["foldLine"] = "Fold Line",
                ["unfoldLine"] = "Unfold Line",
                ["previewButtonTitle"] = "Preview",
                ["closeButtonTitle"] = "Close",
                ["cmdClickToFollow"] = "Ctrl-click to follow",
                ["cmdClickToToggleTodo"] = "Ctrl-click to toggle todo",
            },
        };

        if (_settings.EditorUndoGroupingInterval is { } interval)
        {
            config["undoGroupingInterval"] = interval;
        }

        if (_settings.EditorHeaderFontSizeDiffs is { } diffs)
        {
            config["headerFontSizeDiffs"] = diffs;
        }

        if (_settings.EditorVisibleWhitespaceCharacter is { } whitespace)
        {
            config["visibleWhitespaceCharacter"] = whitespace;
        }

        if (_settings.EditorVisibleLineBreakCharacter is { } lineBreak)
        {
            config["visibleLineBreakCharacter"] = lineBreak;
        }

        if (_settings.EditorSearchNormalizers is { } normalizers)
        {
            config["searchNormalizers"] = normalizers;
        }

        return JsonSerializer.Serialize(config);
    }

    private static Dictionary<string, string> StandardDirectories()
    {
        return new Dictionary<string, string>
        {
            ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["documents"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["library"] = AppPaths.UserDataDirectory,
            ["caches"] = AppPaths.CacheDirectory,
            ["temporary"] = Path.GetTempPath(),
        };
    }

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.36.0";

    private static string AppBuild =>
        Assembly.GetExecutingAssembly().GetName().Version?.Build.ToString() ?? "1";

    // MARK: - Native dispatch

    private Task<object?> DispatchNativeCallAsync(string moduleName, string methodName, JsonElement? parameters)
    {
        return _dispatcher.DispatchAsync(moduleName, methodName, new BridgeParams(parameters));
    }

    // MARK: - IEditorHost

    public void HandleWindowDidLoad()
    {
        _document.IsEditorReady = true;
        _ = PushDocumentAsync();
    }

    public void HandleWindowClose() => Close();

    public void HandleEditorBecameIdle()
    {
        if (!_settings.EditorAutoSaveWhenIdle || _document.FilePath is null || !_document.IsDirty)
        {
            return;
        }

        _ = SaveDocumentAsync();
    }

    public void HandleViewDidUpdate(BridgeParams args)
    {
        var contentEdited = args.GetBool("contentEdited");
        var isDirty = args.GetBool("isDirty");

        if (contentEdited)
        {
            _document.HasBeenEdited = true;
        }

        _document.IsDirty = isDirty || (_document.HasBeenEdited && _document.FilePath is null);
        UpdateTitle();

        if (args.Get("selectedLineColumn") is { } lineColumn)
        {
            UpdatePosition(lineColumn);
        }
    }

    public void HandleLinkClicked(string link) => ShellLauncher.TryOpen(link);

    public void HandleBackgroundColor(int color, double alpha)
    {
        // Keeps the window chrome in sync so resizing does not flash a white background.
        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)((color >> 16) & 0xFF),
            (byte)((color >> 8) & 0xFF),
            (byte)(color & 0xFF)));

        brush.Freeze();
        Background = brush;

        if (_core is not null)
        {
            WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
                (int)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
                (byte)((color >> 16) & 0xFF),
                (byte)((color >> 8) & 0xFF),
                (byte)(color & 0xFF));
        }
    }

    public void HandleCompositionEnded()
    {
        // Nothing to do, the web editor owns the composition state.
    }

    public void PlayBeep() => System.Media.SystemSounds.Beep.Play();

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => _core?.Reload());
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith($"https://{VirtualHost}/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        ShellLauncher.TryOpen(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        ShellLauncher.TryOpen(e.Uri);
    }

    // MARK: - Document plumbing

    private string LineBreakCharacters => _document.LineEnding switch
    {
        LineEnding.CRLF => "\r\n",
        LineEnding.CR => "\r",
        _ => "\n",
    };

    private int LineEndingsValue => _document.LineEnding switch
    {
        LineEnding.CRLF => 2,
        LineEnding.CR => 3,
        _ => 1,
    };

    private async Task PushDocumentAsync()
    {
        var text = _document.FilePath is null
            ? string.Empty
            : SafeReadDocumentText();

        _document.IsResetting = true;
        try
        {
            object? selection = _document.PendingSelection is { } pending
                ? new { anchor = pending.Anchor, head = pending.Head }
                : null;

            await InvokeWebAsync("core.resetEditor", new
            {
                text,
                selectionRange = selection,
                documentChanged = true,
            });

            await InvokeWebAsync("history.markContentClean");
            await InvokeWebAsync("config.setDefaultLineBreak", new { lineBreak = LineBreakCharacters });
            await InvokeWebAsync("lineEndings.setLineEndings", new { lineEndings = LineEndingsValue });
            await InvokeWebAsync("api.notifyAppReady");
        }
        finally
        {
            _document.IsResetting = false;
            _document.PendingSelection = null;
        }

        UpdateStatusBar();
        UpdateTitle();
    }

    private string SafeReadDocumentText()
    {
        try
        {
            return _document.ReadText();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Unable to read the file:\n\n{exception.Message}", "MarkEdit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return string.Empty;
        }
    }

    private async Task<string?> GetEditorTextAsync()
    {
        var result = await InvokeWebAsync("core.getEditorText");
        return result is { ValueKind: JsonValueKind.String } ? result.Value.GetString() : null;
    }

    private void UpdateTitle()
    {
        var marker = _document.IsDirty ? "*" : string.Empty;
        Title = $"{marker}{_document.DisplayName} - MarkEdit";
    }

    private void UpdateStatusBar()
    {
        StatusPath.Text = _document.FilePath ?? "Untitled";
        StatusEncoding.Text = DescribeEncoding(_document.Encoding);
        StatusLineEnding.Text = _document.LineEnding switch
        {
            LineEnding.CRLF => "CRLF",
            LineEnding.CR => "CR",
            _ => "LF",
        };
    }

    private static string DescribeEncoding(Encoding encoding)
    {
        return encoding.CodePage switch
        {
            65001 => encoding.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            12000 or 12001 => "UTF-32",
            _ => encoding.WebName.ToUpperInvariant(),
        };
    }

    private void UpdatePosition(JsonElement lineColumn)
    {
        var line = lineColumn.TryGetProperty("lineNumber", out var lineElement) ? lineElement.GetInt32() : 0;
        var column = lineColumn.TryGetProperty("columnText", out var columnElement) ? columnElement.GetString() : null;
        StatusPosition.Text = column is null ? $"Ln {line}" : $"Ln {line}, {column}";
    }

    // MARK: - Theme and appearance

    private void ApplyTheme(string theme)
    {
        PostWeb("config.setTheme", new { name = theme });

        var dark = theme.Contains("dark", StringComparison.OrdinalIgnoreCase);
        Background = new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        RefreshMenuChecks();
    }

    private void SetFontSize(double size)
    {
        _settings.FontSize = Math.Clamp(size, 8, 72);
        _settings.Save();
        PostWeb("config.setFontSize", new { fontSize = _settings.FontSize });
    }

    private void SetReadOnly(bool enabled)
    {
        _isReadOnly = enabled;
        PostWeb("config.setReadOnlyMode", new { enabled });
        StatusReadOnly.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshMenuChecks();
    }
}
