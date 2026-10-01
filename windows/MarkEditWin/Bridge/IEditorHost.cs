using System.Text.Json;
using System.Windows;
using MarkEditWin.Services;

namespace MarkEditWin.Bridge;

/// <summary>
/// Everything the native modules need from the window that hosts the editor.
/// </summary>
public interface IEditorHost
{
    DocumentSession Document { get; }

    AppSettings Settings { get; }

    Window? Window { get; }

    /// <summary>Invokes a web module function and awaits its result.</summary>
    Task<JsonElement?> InvokeWebAsync(string path, object? message = null);

    /// <summary>Invokes a web module function without waiting for the result.</summary>
    void PostWeb(string path, object? message = null);

    void HandleWindowDidLoad();

    void HandleWindowClose();

    void HandleEditorBecameIdle();

    void HandleViewDidUpdate(BridgeParams args);

    void HandleLinkClicked(string link);

    void HandleBackgroundColor(int color, double alpha);

    void HandleCompositionEnded();

    /// <summary>Saves the document, showing a save panel when it has no file yet.</summary>
    Task<bool> SaveDocumentAsync();

    void PlayBeep();

    void AddUserMenuItems(JsonElement items);

    void ShowContextMenu(JsonElement items, double x, double y);
}
