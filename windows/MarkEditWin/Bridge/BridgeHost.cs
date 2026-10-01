using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace MarkEditWin.Bridge;

/// <summary>
/// Transport between the web editor core and the native host.
///
/// Web to native: the CoreEditor calls <c>window.webkit.messageHandlers.bridge.postMessage(...)</c>
/// with <c>{ moduleName, methodName, parameters }</c>, where <c>parameters</c> is a JSON string.
/// The shim adds a call id, and we answer with <c>{ __markedit: "reply", callID, result, error }</c>.
///
/// Native to web: we post <c>{ __markedit: "invoke", callID, path, message }</c> and await
/// <c>{ __markedit: "invokeResult", callID, result, error }</c>. The same mechanism is used for
/// fire-and-forget calls, the result is simply ignored.
/// </summary>
public sealed class BridgeHost
{
    /// <summary>
    /// Null values are omitted: the editor core distinguishes between an absent optional argument
    /// and an explicit null (for example <c>resetEditor</c> reads <c>selectionRange.anchor</c>).
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CoreWebView2 _core;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pendingInvokes = new();
    private readonly Func<string, string, JsonElement?, Task<object?>> _dispatch;
    private long _nextInvokeId;

    public BridgeHost(CoreWebView2 core, Func<string, string, JsonElement?, Task<object?>> dispatch)
    {
        _core = core;
        _dispatch = dispatch;
        _core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Raised when the page asks the host to run a menu command by name.</summary>
    public event Action<string>? CommandReceived;

    public void Dispose()
    {
        _core.WebMessageReceived -= OnWebMessageReceived;
    }

    /// <summary>
    /// Calls a function on the page, e.g. <c>core.resetEditor</c>, and awaits its result.
    /// </summary>
    public async Task<JsonElement?> InvokeAsync(string path, object? message = null)
    {
        var callId = Interlocked.Increment(ref _nextInvokeId);
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingInvokes[callId] = completion;

        var payload = JsonSerializer.Serialize(new
        {
            __markedit = "invoke",
            callID = callId,
            path,
            message,
        }, SerializerOptions);

        try
        {
            _core.PostWebMessageAsJson(payload);
            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            _pendingInvokes.TryRemove(callId, out _);
        }
    }

    /// <summary>
    /// Calls a function on the page without waiting for the result.
    /// </summary>
    public void Post(string path, object? message = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            __markedit = "invoke",
            callID = 0,
            path,
            message,
        }, SerializerOptions);

        _core.PostWebMessageAsJson(payload);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try
        {
            raw = e.WebMessageAsJson;
        }
        catch (Exception)
        {
            return;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(raw);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var kind = root.TryGetProperty("__markedit", out var kindElement) ? kindElement.GetString() : null;

        if (kind == "command")
        {
            if (root.TryGetProperty("command", out var commandElement) && commandElement.ValueKind == JsonValueKind.String)
            {
                var command = commandElement.GetString();
                if (!string.IsNullOrEmpty(command))
                {
                    CommandReceived?.Invoke(command);
                }
            }

            return;
        }

        if (kind == "invokeResult")
        {
            CompleteInvoke(root);
            return;
        }

        if (kind != "native")
        {
            return;
        }

        await HandleNativeCallAsync(root);
    }

    private void CompleteInvoke(JsonElement root)
    {
        if (!root.TryGetProperty("callID", out var callIdElement) || !callIdElement.TryGetInt64(out var callId))
        {
            return;
        }

        if (!_pendingInvokes.TryRemove(callId, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
        {
            completion.TrySetException(new InvalidOperationException(errorElement.GetString()));
            return;
        }

        completion.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : null);
    }

    private async Task HandleNativeCallAsync(JsonElement root)
    {
        long callId = 0;
        if (root.TryGetProperty("callID", out var callIdElement) && callIdElement.TryGetInt64(out var parsedCallId))
        {
            callId = parsedCallId;
        }

        object? result = null;
        string? error = null;

        try
        {
            var moduleName = root.TryGetProperty("moduleName", out var moduleElement) ? moduleElement.GetString() : null;
            var methodName = root.TryGetProperty("methodName", out var methodElement) ? methodElement.GetString() : null;

            if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(methodName))
            {
                throw new InvalidOperationException("Invalid native bridge message.");
            }

            JsonElement? parameters = null;
            if (root.TryGetProperty("parameters", out var parametersElement) &&
                parametersElement.ValueKind == JsonValueKind.String)
            {
                var json = parametersElement.GetString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var document = JsonDocument.Parse(json);
                    parameters = document.RootElement.Clone();
                }
            }

            result = await _dispatch(moduleName, methodName, parameters);
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var reply = JsonSerializer.Serialize(new
        {
            __markedit = "reply",
            callID = callId,
            result,
            error,
        }, SerializerOptions);

        try
        {
            _core.PostWebMessageAsJson(reply);
        }
        catch (Exception)
        {
            // The window may have been closed while the call was in flight.
        }
    }
}
