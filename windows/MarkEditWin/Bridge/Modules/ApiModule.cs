using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using MarkEditWin.Services;
using MarkEditWin.Views;
using Microsoft.Win32;

namespace MarkEditWin.Bridge.Modules;

/// <summary>
/// The <c>api</c> native module: documents, file system access, dialogs, clipboard and app control.
/// </summary>
public sealed class ApiModule(IEditorHost host) : INativeModule
{
    private readonly IEditorHost _host = host;

    public string Name => "api";

    public async Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        switch (method)
        {
            case "saveDocument":
                return await SaveDocumentAsync();

            case "closeDocument":
                return CloseDocument();

            case "addMainMenuItems":
                if (parameters.Get("items") is { } menuSpecs)
                {
                    _host.AddUserMenuItems(menuSpecs);
                }

                return null;

            case "showContextMenu":
                {
                    var items = parameters.Get("items");
                    if (items is { } menuItems)
                    {
                        var location = parameters.Get("location");
                        var x = location?.TryGetProperty("x", out var xElement) == true ? xElement.GetDouble() : 0;
                        var y = location?.TryGetProperty("y", out var yElement) == true ? yElement.GetDouble() : 0;
                        _host.ShowContextMenu(menuItems, x, y);
                    }

                    return null;
                }

            case "showAlert":
                {
                    var title = parameters.GetString("title");
                    var message = parameters.GetString("message");
                    var buttons = parameters.GetStringArray("buttons") ?? [];
                    return MessageDialog.Show(_host.Window, title, message, buttons);
                }

            case "showTextBox":
                {
                    var title = parameters.GetString("title");
                    var placeholder = parameters.GetString("placeholder");
                    var defaultValue = parameters.GetString("defaultValue");
                    return TextInputDialog.Show(_host.Window, title, placeholder, defaultValue);
                }

            case "showSavePanel":
                return ShowSavePanel(parameters);

            // macOS Services have no Windows equivalent.
            case "runService":
                return false;

            case "openFile":
                return OpenFile(parameters.GetString("path"));

            case "createFile":
                return CreateFile(parameters.Get("options"));

            case "deleteFile":
                return DeleteFile(parameters.GetString("path"));

            case "moveFile":
                return MoveFile(parameters.Get("options"));

            case "revealFile":
                return RevealFile(parameters.GetString("path"));

            case "listFiles":
                return ListFiles(parameters.GetString("path"));

            case "getFileContent":
                return GetFileContent(parameters.GetString("path"));

            case "getFileObject":
                return GetFileObject(parameters.GetString("path"));

            case "getFileInfo":
                return GetFileInfo(parameters.GetString("path"));

            // Document versioning is an NSFileVersion feature, Windows has no direct equivalent.
            case "getFileVersions":
            case "getFileVersionContent":
                return null;

            case "restoreFileVersion":
            case "deleteLocalFileVersions":
                return false;

            case "getPasteboardItems":
                return GetPasteboardItems();

            case "getPasteboardString":
                return GetPasteboardString();

            case "terminateApp":
                Application.Current?.Shutdown();
                return null;

            case "relaunchApp":
                Relaunch();
                return null;

            case "playSystemBeep":
                _host.PlayBeep();
                return null;

            default:
                return null;
        }
    }

    // MARK: - Documents

    private async Task<bool> SaveDocumentAsync()
    {
        return await _host.SaveDocumentAsync();
    }

    private bool CloseDocument()
    {
        _host.HandleWindowClose();
        return true;
    }

    // MARK: - Dialogs

    private bool ShowSavePanel(BridgeParams parameters)
    {
        var options = parameters.Get("options");
        if (options is null)
        {
            return false;
        }

        var fileName = options.Value.TryGetProperty("fileName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;

        var text = options.Value.TryGetProperty("string", out var stringElement) && stringElement.ValueKind == JsonValueKind.String
            ? stringElement.GetString()
            : null;

        var base64 = options.Value.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
            ? dataElement.GetString()
            : null;

        var dialog = new SaveFileDialog
        {
            Title = "Save",
            InitialDirectory = _host.Settings.DefaultSaveDirectory,
            FileName = fileName ?? "Untitled",
            Filter = "All Files (*.*)|*.*",
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(_host.Window) != true)
        {
            return false;
        }

        try
        {
            if (base64 is not null)
            {
                File.WriteAllBytes(dialog.FileName, Convert.FromBase64String(base64));
            }
            else
            {
                File.WriteAllText(dialog.FileName, text ?? string.Empty, new UTF8Encoding(false));
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // MARK: - File system

    private bool OpenFile(string? path)
    {
        return ShellLauncher.TryOpen(path ?? string.Empty);
    }

    private bool CreateFile(JsonElement? options)
    {
        if (options is null)
        {
            return false;
        }

        var path = options.Value.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var isDirectory = options.Value.TryGetProperty("isDirectory", out var dirElement) && dirElement.ValueKind == JsonValueKind.True;
        var overwrites = options.Value.TryGetProperty("overwrites", out var overwriteElement) && overwriteElement.ValueKind == JsonValueKind.True;

        var text = options.Value.TryGetProperty("string", out var stringElement) && stringElement.ValueKind == JsonValueKind.String
            ? stringElement.GetString()
            : null;

        var base64 = options.Value.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
            ? dataElement.GetString()
            : null;

        try
        {
            if (isDirectory)
            {
                if (Directory.Exists(path) && overwrites)
                {
                    Directory.Delete(path, true);
                }

                Directory.CreateDirectory(path);
                return true;
            }

            var exists = File.Exists(path);
            if (exists && !overwrites)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (base64 is not null)
            {
                File.WriteAllBytes(path, Convert.FromBase64String(base64));
            }
            else
            {
                File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(false));
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool DeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return true;
            }
        }
        catch (Exception)
        {
            // Fall through and report the failure.
        }

        return false;
    }

    private bool MoveFile(JsonElement? options)
    {
        if (options is null)
        {
            return false;
        }

        var source = options.Value.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : null;
        var destination = options.Value.TryGetProperty("destination", out var destinationElement) ? destinationElement.GetString() : null;
        var overwrites = options.Value.TryGetProperty("overwrites", out var overwriteElement) && overwriteElement.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        try
        {
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (File.Exists(destination) || Directory.Exists(destination))
            {
                if (!overwrites)
                {
                    return false;
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                else
                {
                    Directory.Delete(destination, true);
                }
            }

            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool RevealFile(string? path)
    {
        var target = ResolvePath(path);
        return target is not null && ShellLauncher.TryReveal(target);
    }

    private string[]? ListFiles(string? path)
    {
        var target = ResolvePath(path);
        if (target is null || !Directory.Exists(target))
        {
            return null;
        }

        try
        {
            return Directory.GetFileSystemEntries(target).Select(Path.GetFileName).OfType<string>().ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? GetFileContent(string? path)
    {
        var target = ResolvePath(path);
        if (target is null || !File.Exists(target))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(target);
            var text = DecodeText(bytes);
            return text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? GetFileObject(string? path)
    {
        var target = ResolvePath(path);
        if (target is null || !File.Exists(target))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(target);
            var extension = Path.GetExtension(target).TrimStart('.').ToLowerInvariant();
            var payload = new Dictionary<string, object?>
            {
                ["data"] = Convert.ToBase64String(bytes),
                ["filenameExtension"] = extension.Length == 0 ? null : extension,
                ["mimeType"] = MimeTypeFor(extension),
                ["typeIdentifier"] = MimeTypeFor(extension),
            };

            return JsonSerializer.Serialize(payload);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? GetFileInfo(string? path)
    {
        var target = ResolvePath(path);
        if (target is null)
        {
            return null;
        }

        try
        {
            var isDirectory = Directory.Exists(target);
            var info = isDirectory ? new DirectoryInfo(target) as FileSystemInfo : new FileInfo(target);
            if (!info.Exists)
            {
                return null;
            }

            var payload = new Dictionary<string, object?>
            {
                ["filePath"] = info.FullName,
                ["fileSize"] = isDirectory ? 0d : ((FileInfo)info).Length,
                ["creationDate"] = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                ["modificationDate"] = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                ["parentPath"] = Path.GetDirectoryName(info.FullName) ?? string.Empty,
                ["isDirectory"] = isDirectory,
            };

            return JsonSerializer.Serialize(payload);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // MARK: - Clipboard

    private string? GetPasteboardItems()
    {
        try
        {
            var items = new List<Dictionary<string, string?>>();

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                items.Add(new Dictionary<string, string?>
                {
                    ["type"] = "text/plain",
                    ["string"] = text,
                    ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
                });
            }

            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is not null)
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    items.Add(new Dictionary<string, string?>
                    {
                        ["type"] = "image/png",
                        ["data"] = Convert.ToBase64String(stream.ToArray()),
                    });
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var list = new List<string>();
                foreach (string? file in files)
                {
                    if (file is not null)
                    {
                        list.Add(new Uri(file).AbsoluteUri);
                    }
                }

                if (list.Count > 0)
                {
                    var payload = string.Join("\r\n", list);
                    items.Add(new Dictionary<string, string?>
                    {
                        ["type"] = "text/uri-list",
                        ["string"] = payload,
                        ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)),
                    });
                }
            }

            return items.Count == 0 ? null : JsonSerializer.Serialize(items);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? GetPasteboardString()
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

    // MARK: - App

    private void Relaunch()
    {
        // Relaunch is best effort, launching goes through the validated shell helper.
        ShellLauncher.TryRelaunch();
        Application.Current?.Shutdown();
    }

    // MARK: - Helpers

    private string? ResolvePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return _host.Document.FilePath;
    }

    private static string DecodeText(byte[] bytes)
    {
        return DocumentSession.Decode(bytes);
    }

    private static string MimeTypeFor(string extension)
    {
        return extension switch
        {
            "md" or "markdown" or "txt" => "text/plain",
            "json" => "application/json",
            "html" or "htm" => "text/html",
            "css" => "text/css",
            "js" or "mjs" => "text/javascript",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "pdf" => "application/pdf",
            "yaml" or "yml" => "application/yaml",
            _ => "application/octet-stream",
        };
    }
}
