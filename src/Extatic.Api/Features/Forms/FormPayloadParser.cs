using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Extatic.Api.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Extatic.Api.Features.Forms;

/// <summary>A parsed submission body. <c>IsHtmlForm</c> is true for urlencoded / multipart posts.</summary>
public record ParsedSubmission(JsonObject Data, bool IsHtmlForm);

/// <summary>
/// Turns a raw submission body into a JSON object. JSON bodies must be an object. HTML form posts
/// (<c>application/x-www-form-urlencoded</c>, <c>multipart/form-data</c> without files) become an
/// object of strings; repeated keys (or keys ending in <c>[]</c>) become arrays.
/// </summary>
public static class FormPayloadParser
{
    public const int MaxPayloadBytes = 64 * 1024;
    private const int MaxFields = 500;

    public static async Task<ParsedSubmission> ParseAsync(string? contentType, byte[] body)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            throw new BadRequestException("Content-Type must be application/json, application/x-www-form-urlencoded or multipart/form-data");

        var media = mediaType.MediaType.Value?.ToLowerInvariant();
        if (media == "application/json" || (media?.EndsWith("+json") ?? false))
            return new ParsedSubmission(ParseJson(body), false);
        if (media == "application/x-www-form-urlencoded")
            return new ParsedSubmission(ParseUrlEncoded(body), true);
        if (media == "multipart/form-data")
            return new ParsedSubmission(await ParseMultipartAsync(mediaType, body), true);

        throw new BadRequestException("Content-Type must be application/json, application/x-www-form-urlencoded or multipart/form-data");
    }

    private static JsonObject ParseJson(byte[] body)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            throw new BadRequestException("Request body is not valid JSON");
        }
        return node as JsonObject ?? throw new BadRequestException("Request body must be a JSON object");
    }

    private static JsonObject ParseUrlEncoded(byte[] body)
    {
        var fields = new FieldAccumulator();
        using var reader = new FormReader(Encoding.UTF8.GetString(body));
        while (reader.ReadNextPair() is { } pair)
            fields.Add(pair.Key, pair.Value);
        return fields.ToJson();
    }

    private static async Task<JsonObject> ParseMultipartAsync(MediaTypeHeaderValue mediaType, byte[] body)
    {
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
            throw new BadRequestException("Missing multipart boundary");

        var fields = new FieldAccumulator();
        var reader = new MultipartReader(boundary, new MemoryStream(body));
        try
        {
            while (await reader.ReadNextSectionAsync() is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.DispositionType.Equals("form-data"))
                    throw new BadRequestException("Invalid multipart section");

                using var streamReader = new StreamReader(section.Body, Encoding.UTF8);
                var value = await streamReader.ReadToEndAsync();
                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;

                if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
                {
                    // An empty <input type="file"> still sends a part with an empty filename: ignore it.
                    var fileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                    if (string.IsNullOrEmpty(fileName) && string.IsNullOrEmpty(disposition.FileNameStar.Value)
                        && value.Length == 0)
                        continue;
                    throw new BadRequestException("File uploads are not supported by forms");
                }
                fields.Add(name, value);
            }
        }
        catch (IOException)
        {
            throw new BadRequestException("Malformed multipart body");
        }
        catch (InvalidDataException)
        {
            throw new BadRequestException("Malformed multipart body");
        }
        return fields.ToJson();
    }

    /// <summary>
    /// Coerces string values (from an HTML form post) to the types the schema declares for each
    /// top-level property: integer, number, boolean, or array (wrapping a single value). Empty
    /// strings for non-string properties are dropped, so an empty optional number field counts as
    /// "not supplied". Values that cannot be converted are left as-is for validation to report.
    /// </summary>
    public static void CoerceToSchema(JsonObject data, string schemaJson)
    {
        JsonObject? properties;
        try
        {
            properties = JsonNode.Parse(schemaJson)?["properties"] as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (properties is null) return;

        foreach (var key in data.Select(p => p.Key).ToList())
        {
            if (properties[key] is not JsonObject propertySchema) continue;
            var coerced = Coerce(data[key], propertySchema, out var remove);
            if (remove) data.Remove(key);
            else data[key] = coerced;
        }
    }

    private static JsonNode? Coerce(JsonNode? value, JsonObject propertySchema, out bool remove)
    {
        remove = false;
        var types = DeclaredTypes(propertySchema);
        if (types.Count == 0) return Detach(value);

        if (types.Contains("array"))
        {
            var items = propertySchema["items"] as JsonObject;
            var elements = value is JsonArray array ? array.Select(Detach).ToList() : [Detach(value)];
            var result = new JsonArray();
            foreach (var element in elements)
            {
                if (items is null) { result.Add(element); continue; }
                var coerced = Coerce(element, items, out var dropElement);
                if (!dropElement) result.Add(coerced);
            }
            // A lone empty value for an optional array is "not supplied" (unless the array is allowed to be a string too).
            if (result.Count == 0 && value is not JsonArray && !types.Contains("string")) remove = true;
            return result;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text))
            return Detach(value);
        if (types.Contains("string")) return Detach(value);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            remove = true;
            return null;
        }
        if (types.Contains("integer") && long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            return JsonValue.Create(l);
        if (types.Contains("number") && decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return JsonValue.Create(d);
        if (types.Contains("boolean"))
        {
            switch (trimmed.ToLowerInvariant())
            {
                case "true" or "on" or "yes" or "1": return JsonValue.Create(true);
                case "false" or "off" or "no" or "0": return JsonValue.Create(false);
            }
        }
        if (types.Contains("null") && trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;
        return Detach(value);
    }

    private static HashSet<string> DeclaredTypes(JsonObject schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        switch (schema["type"])
        {
            case JsonValue v when v.TryGetValue<string>(out var t):
                set.Add(t);
                break;
            case JsonArray a:
                foreach (var item in a)
                    if (item is JsonValue iv && iv.TryGetValue<string>(out var it)) set.Add(it);
                break;
        }
        return set;
    }

    private static JsonNode? Detach(JsonNode? node) => node?.DeepClone();

    private sealed class FieldAccumulator
    {
        private readonly List<string> order = [];
        private readonly Dictionary<string, List<string>> values = new(StringComparer.Ordinal);
        private readonly HashSet<string> forceArray = new(StringComparer.Ordinal);
        private int count;

        public void Add(string rawKey, string value)
        {
            if (++count > MaxFields)
                throw new BadRequestException($"Too many fields (max {MaxFields})");
            var key = rawKey;
            if (key.EndsWith("[]", StringComparison.Ordinal) && key.Length > 2)
            {
                key = key[..^2];
                forceArray.Add(key);
            }
            if (key.Length == 0) return;
            if (!values.TryGetValue(key, out var list))
            {
                values[key] = list = [];
                order.Add(key);
            }
            list.Add(value);
        }

        public JsonObject ToJson()
        {
            var result = new JsonObject();
            foreach (var key in order)
            {
                var list = values[key];
                result[key] = list.Count == 1 && !forceArray.Contains(key)
                    ? JsonValue.Create(list[0])
                    : new JsonArray(list.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            }
            return result;
        }
    }
}
