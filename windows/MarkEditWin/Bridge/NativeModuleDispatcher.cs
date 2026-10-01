namespace MarkEditWin.Bridge;

/// <summary>
/// A native module that the web editor core can call through the bridge.
/// </summary>
public interface INativeModule
{
    /// <summary>Bridge name, e.g. <c>core</c> or <c>api</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Handles a method call. The returned value is serialized to JSON and handed back to the
    /// page; return <c>null</c> for methods that do not produce a value.
    /// </summary>
    Task<object?> InvokeAsync(string method, BridgeParams parameters);
}

/// <summary>
/// Routes bridge calls coming from the page to the matching native module.
/// </summary>
public sealed class NativeModuleDispatcher
{
    private readonly Dictionary<string, INativeModule> _modules = new(StringComparer.Ordinal);

    public NativeModuleDispatcher(params INativeModule[] modules)
    {
        foreach (var module in modules)
        {
            _modules[module.Name] = module;
        }
    }

    public Task<object?> DispatchAsync(string moduleName, string methodName, BridgeParams parameters)
    {
        if (!_modules.TryGetValue(moduleName, out var module))
        {
            throw new InvalidOperationException($"Invalid native module name: {moduleName}");
        }

        return module.InvokeAsync(methodName, parameters);
    }
}
