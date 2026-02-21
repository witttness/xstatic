using System.Text.Json;
using Json.Schema;
using Extatic.Api.Services;

namespace Extatic.Api.Validation;

public class JsonSchemaValidator
{
    public List<ValidationError> Validate(string schemaJson, JsonElement data)
    {
        var schema = JsonSchema.FromText(schemaJson);
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
        var results = schema.Evaluate(data, options);

        if (results.IsValid) return [];

        return (results.Details ?? [])
            .Where(d => !d.IsValid && d.Errors?.Count > 0)
            .SelectMany(d => d.Errors!.Select(e =>
                new ValidationError(d.InstanceLocation.ToString(), e.Value)))
            .ToList();
    }
}
