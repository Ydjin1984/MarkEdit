using System.Text.Json;

namespace MarkEditWin.Bridge;

/// <summary>
/// Convenience accessors over the JSON object that the web editor passes to native modules.
/// </summary>
public readonly struct BridgeParams(JsonElement? element)
{
    private readonly JsonElement? _element = element;

    public bool IsEmpty => _element is null || _element.Value.ValueKind != JsonValueKind.Object;

    public string? GetString(string name)
    {
        if (TryGet(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    public string GetString(string name, string fallback) => GetString(name) ?? fallback;

    public int? GetInt(string name)
    {
        if (TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
        {
            return result;
        }

        return null;
    }

    public int GetInt(string name, int fallback) => GetInt(name) ?? fallback;

    public double? GetDouble(string name)
    {
        if (TryGet(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result))
        {
            return result;
        }

        return null;
    }

    public bool GetBool(string name, bool fallback = false)
    {
        if (TryGet(name, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }

        return fallback;
    }

    public JsonElement? Get(string name) => TryGet(name, out var value) ? value.Clone() : null;

    /// <summary>Returns the raw JSON of a nested object, or null when it is absent.</summary>
    public string? GetRawJson(string name)
    {
        if (TryGet(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            return value.GetRawText();
        }

        return null;
    }

    public JsonElement? GetProperty(string name) => Get(name);

    public string[]? GetStringArray(string name)
    {
        if (!TryGet(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        if (_element is null || _element.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return _element.Value.TryGetProperty(name, out value);
    }
}
