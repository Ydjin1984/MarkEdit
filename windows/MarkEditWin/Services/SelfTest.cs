using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MarkEditWin.Services;

/// <summary>
/// Headless verification of the native bridge.
///
/// <c>MarkEdit.exe --self-test &lt;document&gt; &lt;report&gt;</c> loads the editor with the real
/// CoreEditor bundle, then exercises the web modules through the bridge and writes a report.
/// It is used by the build script to prove that the port is wired up correctly.
/// </summary>
public static class SelfTest
{
    public const string Flag = "--self-test";

    public static async Task<int> RunAsync(string documentPath, string reportPath)
    {
        var report = new StringBuilder();
        var failures = 0;
        var checks = new List<object>();

        void Check(string name, bool passed, string detail)
        {
            if (!passed)
            {
                failures++;
            }

            report.AppendLine($"{(passed ? "PASS" : "FAIL")}  {name}: {detail}");
            checks.Add(new { name, passed, detail });
        }

        MainWindow? window = null;

        try
        {
            AppPaths.EnsureCreated();
            var settings = new AppSettings();

            window = new MainWindow(settings, documentPath)
            {
                Left = -20000,
                Top = -20000,
                ShowInTaskbar = false,
            };

            window.Show();

            // Wait for the page to report that the editor finished loading.
            var ready = false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (window.Document.IsEditorReady)
                {
                    ready = true;
                    break;
                }

                await Task.Delay(200);
            }

            Check("editor.loaded", ready, ready ? "page reported windowDidLoad" : "timed out waiting for windowDidLoad");

            if (!ready)
            {
                return Finish(report, checks, failures, reportPath);
            }

            // The host pushes the document through core.resetEditor, so reading it back proves
            // the whole loop works: page -> native -> page -> native.
            var expected = await File.ReadAllTextAsync(documentPath, Encoding.UTF8);

            // The host pushes the document asynchronously after windowDidLoad. Waiting must not
            // block the UI thread, the bridge replies are pumped by the same dispatcher.
            await Task.Delay(2500);

            Check("web.errors", window.WebErrors.Count == 0, window.WebErrors.Count == 0 ? "none" : string.Join(" | ", window.WebErrors));

            var pageState = await window.ExecuteScriptForTestAsync(
                "JSON.stringify({ title: document.title, modules: typeof window.webModules, editor: typeof window.editor })");
            Check("page.state", pageState?.Contains("object") == true, pageState ?? "no response");

            string? editorText = null;
            for (var attempt = 0; attempt < 20 && string.IsNullOrEmpty(editorText); attempt++)
            {
                editorText = await ReadEditorTextAsync(window);
                if (string.IsNullOrEmpty(editorText))
                {
                    await Task.Delay(500);
                }
            }
            Check(
                "document.roundtrip",
                Normalize(editorText) == Normalize(expected),
                $"editor holds {editorText?.Length ?? 0} chars, file holds {expected.Length}, last web error: {window.LastWebError ?? "none"}");

            // Search is driven from native because the editor core ships an empty search panel.
            await window.InvokeWebAsync("search.updateQuery", new
            {
                options = new
                {
                    search = "CodeMirror",
                    caseSensitive = false,
                    diacriticInsensitive = false,
                    wholeWord = false,
                    literal = false,
                    regexp = false,
                    refocus = false,
                },
            });

            var counter = await window.InvokeWebAsync("search.getCounterInfo");
            var matches = counter is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty("numberOfItems", out var n)
                ? n.GetInt32()
                : -1;

            Check("search.matches", matches > 0, $"numberOfItems = {matches}");

            // A formatting command must change the document.
            await window.InvokeWebAsync("selection.selectWholeDocument");
            await window.InvokeWebAsync("format.toggleBlockquote");
            var afterFormat = await ReadEditorTextAsync(window);
            Check("format.applied", Normalize(afterFormat) != Normalize(editorText), "toggleBlockquote changed the text");

            // Undo has to bring it back.
            await window.InvokeWebAsync("history.undo");
            var afterUndo = await ReadEditorTextAsync(window);
            Check("history.undo", Normalize(afterUndo) == Normalize(editorText), "undo restored the original text");

            // Round trip through the file system.
            var savePath = Path.Combine(Path.GetTempPath(), $"markedit-selftest-{Guid.NewGuid():N}.md");
            var saved = await window.SaveToPathAsync(savePath);
            var savedText = saved && File.Exists(savePath) ? await File.ReadAllTextAsync(savePath, Encoding.UTF8) : null;
            Check("document.save", saved && Normalize(savedText) == Normalize(editorText), $"wrote {savePath}");

            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
        catch (Exception exception)
        {
            Check("self-test", false, exception.ToString());
        }
        finally
        {
            window?.Close();
        }

        return Finish(report, checks, failures, reportPath);
    }

    private static int Finish(StringBuilder report, List<object> checks, int failures, string reportPath)
    {
        report.AppendLine();
        report.AppendLine(failures == 0 ? "RESULT: OK" : $"RESULT: {failures} FAILED");

        try
        {
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            File.WriteAllText(reportPath + ".json", JsonSerializer.Serialize(new { failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // The exit code still carries the result.
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<string?> ReadEditorTextAsync(MainWindow window)
    {
        var result = await window.InvokeWebAsync("core.getEditorText");
        return result is { ValueKind: JsonValueKind.String } ? result.Value.GetString() : null;
    }

    private static string Normalize(string? text) => (text ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .TrimEnd('\n');

}
