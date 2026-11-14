using System.Text;
using System.Text.Json;
using Extatic.Api.Services;
using Json.Schema;

namespace Extatic.Api.Features.SchemaLibrary;

/// <summary>Content and metadata rules for library schemas.</summary>
public static class LibrarySchemaRules
{
    public const int MaxSchemaBytes = 256 * 1024;
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxCategoryLength = 100;
    public const int MaxChangeNoteLength = 500;
    public const int MaxTags = 20;
    public const int MaxTagLength = 50;

    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Keywords whose values are data, not subschemas: never scanned for <c>$ref</c>.</summary>
    private static readonly HashSet<string> DataKeywords = ["const", "enum", "default", "examples"];

    private static readonly HashSet<string> RefKeywords = ["$ref", "$dynamicRef", "$recursiveRef"];

    /// <summary>
    /// Schema errors for <paramref name="schema"/> (empty when valid): must be a JSON object, at most
    /// 256 KB, draft 2020-12, buildable by JsonSchema.Net, and contain only fragment (<c>#...</c>) refs so
    /// tenant validation can never trigger a network fetch.
    /// </summary>
    public static List<ValidationError> ValidateSchema(JsonElement schema, string pointer = "schema")
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return [new ValidationError(pointer, "Schema must be a JSON object")];

        var raw = schema.GetRawText();
        if (Encoding.UTF8.GetByteCount(raw) > MaxSchemaBytes)
            return [new ValidationError(pointer, $"Schema must be at most {MaxSchemaBytes / 1024} KB")];

        var errors = new List<ValidationError>();
        if (schema.TryGetProperty("$schema", out var dialect)
            && (dialect.ValueKind != JsonValueKind.String || dialect.GetString()!.TrimEnd('#') != Draft202012))
        {
            errors.Add(new ValidationError($"{pointer}/$schema", $"Only draft 2020-12 is supported ({Draft202012})"));
        }

        FindExternalRefs(schema, pointer, errors);
        if (errors.Count > 0) return errors;

        try
        {
            var built = JsonSchema.Build(schema, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            // Evaluating surfaces lazily resolved $ref problems (e.g. a fragment that points nowhere).
            using var empty = JsonDocument.Parse("{}");
            built.Evaluate(empty.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Flag });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.Add(new ValidationError(pointer, $"Invalid JSON Schema: {ex.Message}"));
        }
        return errors;
    }

    /// <summary>Throws 422 when <paramref name="schema"/> is not an acceptable library schema.</summary>
    public static void EnsureValidSchema(JsonElement schema)
    {
        var errors = ValidateSchema(schema);
        if (errors.Count > 0) throw new ValidationException("Invalid schema", errors);
    }

    private static void FindExternalRefs(JsonElement element, string path, List<ValidationError> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var childPath = $"{path}/{Escape(prop.Name)}";
                    if (RefKeywords.Contains(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        if (!(prop.Value.GetString() ?? "").StartsWith('#'))
                            errors.Add(new ValidationError(childPath,
                                "External references are not allowed; only fragment references starting with '#'"));
                        continue;
                    }
                    if (DataKeywords.Contains(prop.Name)) continue;
                    FindExternalRefs(prop.Value, childPath, errors);
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    FindExternalRefs(item, $"{path}/{i++}", errors);
                break;
        }
    }

    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");

    /// <summary>
    /// Canonical text of a JSON value (object keys sorted ordinally, no whitespace) used to decide
    /// whether a schema's content actually changed.
    /// </summary>
    public static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(element, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Canonicalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Canonicalize(doc.RootElement);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    /// <summary>Trimmed, lower-cased, distinct tags; 422 when too many or too long.</summary>
    public static string[] NormalizeTags(string[]? tags)
    {
        var normalized = (tags ?? [])
            .Select(t => (t ?? "").Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();
        if (normalized.Length > MaxTags)
            throw new ValidationException("Invalid tags", [new ValidationError("tags", $"At most {MaxTags} tags")]);
        if (normalized.Any(t => t.Length > MaxTagLength))
            throw new ValidationException("Invalid tags",
                [new ValidationError("tags", $"Each tag must be at most {MaxTagLength} characters")]);
        return normalized;
    }

    public static string EnsureName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
            throw new ValidationException("Invalid name",
                [new ValidationError("name", $"Name is required (at most {MaxNameLength} characters)")]);
        return trimmed;
    }

    /// <summary>Null keeps the current value (handled by caller); empty/whitespace clears it.</summary>
    public static string? OptionalText(string? value, int maxLength, string pointer)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength)
            throw new ValidationException($"Invalid {pointer}",
                [new ValidationError(pointer, $"At most {maxLength} characters")]);
        return trimmed;
    }

    public static LibrarySchemaVisibility ParseVisibility(string? value)
    {
        var normalized = (value ?? "").Replace("_", "").Replace("-", "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "adminonly" => LibrarySchemaVisibility.AdminOnly,
            "shared" => LibrarySchemaVisibility.Shared,
            _ => throw new ValidationException("Invalid visibility",
                [new ValidationError("visibility", "Must be 'admin_only' or 'shared'")])
        };
    }

    public static string ToWire(this LibrarySchemaVisibility v) => v == LibrarySchemaVisibility.AdminOnly ? "admin_only" : "shared";
    public static string ToWire(this LibrarySchemaScope s) => s == LibrarySchemaScope.Global ? "global" : "app";
}
