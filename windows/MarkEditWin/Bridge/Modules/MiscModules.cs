using System.Text.Json;

namespace MarkEditWin.Bridge.Modules;

/// <summary>
/// The <c>core</c> native module, called by the editor core for window and editor events.
/// </summary>
public sealed class CoreModule(IEditorHost host) : INativeModule
{
    private readonly IEditorHost _host = host;

    public string Name => "core";

    public Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        switch (method)
        {
            case "notifyWindowDidLoad":
                _host.HandleWindowDidLoad();
                break;

            case "notifyWindowClose":
                _host.HandleWindowClose();
                break;

            case "notifyEditorDidBecomeIdle":
                _host.HandleEditorBecameIdle();
                break;

            case "notifyViewDidUpdate":
                _host.HandleViewDidUpdate(parameters);
                break;

            case "notifyCompositionEnded":
                _host.HandleCompositionEnded();
                break;

            case "notifyLinkClicked":
                _host.HandleLinkClicked(parameters.GetString("link") ?? string.Empty);
                break;

            case "notifyBackgroundColorDidChange":
                _host.HandleBackgroundColor(parameters.GetInt("color", 0xFFFFFF), parameters.GetDouble("alpha") ?? 1.0);
                break;

            case "notifyLightWarning":
                _host.PlayBeep();
                break;

            // Window geometry, viewport scale and content metrics are driven by the OS layout
            // engine on Windows, there is nothing to forward.
            case "notifyWindowResize":
            case "notifyWindowMove":
            case "notifyViewportScaleDidChange":
            case "notifyContentHeightDidChange":
            case "notifyContentOffsetDidChange":
                break;

            default:
                return Task.FromResult<object?>(null);
        }

        return Task.FromResult<object?>(null);
    }
}

/// <summary>
/// The <c>completion</c> native module.
///
/// On macOS this drives a native word-completion panel backed by NLP. The Windows port does not
/// ship a native completion panel, so the calls are acknowledged as no-ops: the editor keeps
/// working, it simply does not offer native word completion.
/// </summary>
public sealed class CompletionModule : INativeModule
{
    public string Name => "completion";

    public Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        return Task.FromResult<object?>(null);
    }
}

/// <summary>
/// The <c>tokenizer</c> native module, used for double-click word selection and word-wise
/// navigation for non-ASCII text such as CJK.
/// </summary>
public sealed class TokenizerModule : INativeModule
{
    public string Name => "tokenizer";

    public Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        var anchor = parameters.Get("anchor");
        if (anchor is null)
        {
            return Task.FromResult<object?>(null);
        }

        var text = anchor.Value.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        var pos = anchor.Value.TryGetProperty("pos", out var posElement) && posElement.ValueKind == JsonValueKind.Number
            ? posElement.GetInt32()
            : 0;

        var offset = anchor.Value.TryGetProperty("offset", out var offsetElement) && offsetElement.ValueKind == JsonValueKind.Number
            ? offsetElement.GetInt32()
            : 0;

        pos = Math.Clamp(pos, 0, text.Length);

        switch (method)
        {
            case "tokenize":
                {
                    var (from, to) = Tokenize(text, pos);
                    return Task.FromResult<object?>(new { from, to });
                }

            case "moveWordBackward":
                return Task.FromResult<object?>(offset + MoveBackward(text, pos));

            case "moveWordForward":
                return Task.FromResult<object?>(offset + MoveForward(text, pos));

            default:
                return Task.FromResult<object?>(null);
        }
    }

    /// <summary>Returns the word range around <paramref name="pos"/> as offsets inside the line.</summary>
    private static (int From, int To) Tokenize(string text, int pos)
    {
        if (text.Length == 0)
        {
            return (0, 0);
        }

        var from = pos;
        var to = Math.Min(pos, text.Length);

        while (from > 0 && IsWordCharacter(text[from - 1]))
        {
            from--;
        }

        while (to < text.Length && IsWordCharacter(text[to]))
        {
            to++;
        }

        if (from == to)
        {
            // Landed on a separator, fall back to a single text element.
            from = Math.Min(pos, Math.Max(0, text.Length - 1));
            to = Math.Min(text.Length, from + 1);
        }

        return (from, to);
    }

    private static int MoveBackward(string text, int pos)
    {
        var index = pos;

        // Skip separators first, then the word itself.
        while (index > 0 && !IsWordCharacter(text[index - 1]))
        {
            index--;
        }

        while (index > 0 && IsWordCharacter(text[index - 1]))
        {
            index--;
        }

        return index;
    }

    private static int MoveForward(string text, int pos)
    {
        var index = pos;

        while (index < text.Length && !IsWordCharacter(text[index]))
        {
            index++;
        }

        while (index < text.Length && IsWordCharacter(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// The editor core only calls into the native tokenizer for non-ASCII text, so treating every
    /// non-ASCII letter or digit as part of a word gives CJK text sensible double-click behavior.
    /// </summary>
    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) && character > 0x7F;
    }
}

/// <summary>
/// The <c>foundationModels</c> native module, backed by Apple Intelligence on macOS.
/// Reporting the model as unavailable lets user scripts degrade gracefully.
/// </summary>
public sealed class FoundationModelsModule : INativeModule
{
    private const string UnavailableReason = "Apple Intelligence is not available on Windows.";

    public string Name => "foundationModels";

    public Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        return method switch
        {
            "availability" => Task.FromResult<object?>($"{{\"isAvailable\":false,\"unavailableReason\":\"{UnavailableReason}\"}}"),
            "createSession" => Task.FromResult<object?>(null),
            "isResponding" => Task.FromResult<object?>(false),
            "respondTo" => Task.FromResult<object?>($"{{\"error\":\"{UnavailableReason}\",\"done\":true}}"),
            "streamResponseTo" => Task.FromResult<object?>(null),
            _ => Task.FromResult<object?>(null),
        };
    }
}

/// <summary>
/// The <c>translation</c> native module, backed by the Apple Translation framework on macOS.
/// </summary>
public sealed class TranslationModule : INativeModule
{
    public string Name => "translation";

    public Task<object?> InvokeAsync(string method, BridgeParams parameters)
    {
        return method switch
        {
            "translate" => Task.FromResult<object?>("{\"error\":\"Translation is not available on Windows.\"}"),
            _ => Task.FromResult<object?>(null),
        };
    }
}
