using System.IO;
using System.Text;
using System.Windows;
using MarkEditWin.Services;
using Microsoft.Web.WebView2.Core;

namespace MarkEditWin;

public partial class App : Application
{
    private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
    private static CoreWebView2Environment? _environment;

    private readonly List<MainWindow> _windows = [];

    public static App? CurrentApp => Current as App;

    public AppSettings Settings { get; private set; } = null!;

    public IReadOnlyList<MainWindow> OpenWindows => _windows;

    public static async Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await EnvironmentLock.WaitAsync();
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(AppPaths.CacheDirectory, "WebView2"),
                options: new CoreWebView2EnvironmentOptions
                {
                    // The editor loads KaTeX and Mermaid from a CDN on demand for code previews.
                    AdditionalBrowserArguments = "--disable-features=msWebOOUI,msPdfOOUI",
                });

            return _environment;
        }
        finally
        {
            EnvironmentLock.Release();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Legacy encodings such as Windows-1251 need the code pages provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        AppPaths.EnsureCreated();
        Settings = new AppSettings();
        NativeTheme.StartObserving();

        if (!EnsureWebViewRuntime())
        {
            Shutdown();
            return;
        }

        var selfTestIndex = Array.IndexOf(e.Args, SelfTest.Flag);
        if (selfTestIndex >= 0)
        {
            var documentPath = e.Args.Length > selfTestIndex + 1 ? e.Args[selfTestIndex + 1] : null;
            var reportPath = e.Args.Length > selfTestIndex + 2
                ? e.Args[selfTestIndex + 2]
                : Path.Combine(AppPaths.CacheDirectory, "self-test.txt");

            if (documentPath is null || !File.Exists(documentPath))
            {
                Shutdown(2);
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunSelfTestAsync(documentPath, reportPath);
            return;
        }

        SingleInstanceManager.Initialize();

        var startupFile = e.Args.FirstOrDefault(File.Exists);

        if (!SingleInstanceManager.IsFirstInstance)
        {
            SingleInstanceManager.ForwardToRunningInstance(startupFile);
            Shutdown();
            return;
        }

        SingleInstanceManager.LaunchRequested += path => Dispatcher.InvokeAsync(() => CreateWindow(path));
        CreateWindow(startupFile);
    }

    private async Task RunSelfTestAsync(string documentPath, string reportPath)
    {
        var exitCode = await SelfTest.RunAsync(documentPath, reportPath);
        Shutdown(exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings?.Save();
        base.OnExit(e);
    }

    public MainWindow CreateWindow(string? filePath)
    {
        var window = new MainWindow(Settings, filePath);
        _windows.Add(window);
        window.Show();
        return window;
    }

    public void RemoveWindow(MainWindow window)
    {
        _windows.Remove(window);
    }

    private static bool EnsureWebViewRuntime()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrEmpty(version))
            {
                return true;
            }
        }
        catch (Exception)
        {
            // Reported below with installation instructions.
        }

        MessageBox.Show(
            "MarkEdit needs the Microsoft Edge WebView2 Runtime, which was not found on this PC.\n\n" +
            "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start MarkEdit again.",
            "MarkEdit",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        return false;
    }
}
