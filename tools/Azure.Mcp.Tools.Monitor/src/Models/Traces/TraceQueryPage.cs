// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Mcp.Tools.Monitor.Commands;

namespace Azure.Mcp.Tools.Monitor.Models.Traces;

internal sealed class TraceQueryPage()
{
    public List<JsonElement>? Value { get; set; }

    // Validate continuation after saving rows, even when the server returns a malformed link.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? NextLink { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Properties { get; set; } = [];

    [JsonIgnore]
    public bool HasMoreResults => NextLink switch
    {
        null or { ValueKind: JsonValueKind.Null } => false,
        { ValueKind: JsonValueKind.String } link => !string.IsNullOrWhiteSpace(link.GetString()),
        _ => throw new JsonException("Trace query results contain an invalid 'nextLink'.")
    };

    public void MergePropertiesFrom(TraceQueryPage? previousPage)
    {
        if (previousPage is null)
        {
            return;
        }

        foreach (var property in previousPage.Properties)
        {
            if (!Properties.TryGetValue(property.Key, out var current) ||
                (IsPartialFlag(property.Key) && IsPartialFlagSet(property.Value)))
            {
                Properties[property.Key] = property.Value;
            }
            else if (property.Key == "metadata" && property.Value.ValueKind == JsonValueKind.Object
                && current.ValueKind == JsonValueKind.Object)
            {
                Properties[property.Key] = MergeMetadata(current, property.Value);
            }
        }
    }

    // Coverage can be nested or population-prefixed; later pages must not erase an earlier limit.
    private static JsonElement MergeMetadata(JsonElement current, JsonElement previous)
    {
        var merged = current.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
        foreach (var property in previous.EnumerateObject())
        {
            if (!merged.TryGetValue(property.Name, out var value)
                || (IsPartialFlag(property.Name) && IsPartialFlagSet(property.Value)))
            {
                merged[property.Name] = property.Value;
            }
            else if (value.ValueKind == JsonValueKind.Object && property.Value.ValueKind == JsonValueKind.Object)
            {
                merged[property.Name] = MergeMetadata(value, property.Value);
            }
        }
        return JsonSerializer.SerializeToElement(merged, MonitorJsonContext.Default.DictionaryStringJsonElement);
    }

    internal static bool IsPartialFlag(string name) =>
        name is "isPartialResult" or "isSystemLimitHit" or "hasLimitReached"
        || name.EndsWith(".isPartialResult", StringComparison.Ordinal)
        || name.EndsWith(".isSystemLimitHit", StringComparison.Ordinal)
        || name.EndsWith(".hasLimitReached", StringComparison.Ordinal);

    // Backend metadata can encode coverage flags as boolean strings.
    internal static bool IsPartialFlagSet(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(value.GetString(), out bool isPartial) => isPartial,
        _ => true,
    };
}
