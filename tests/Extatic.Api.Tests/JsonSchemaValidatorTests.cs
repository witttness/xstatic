using System.Text.Json;
using Extatic.Api.Validation;

namespace Extatic.Api.Tests;

public class JsonSchemaValidatorTests
{
    private const string MovieSchema = """
        {
          "$id": "https://example.com/movie.schema.json",
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["title", "director", "releaseDate"],
          "properties": {
            "title": { "type": "string" },
            "director": { "type": "string" },
            "releaseDate": { "type": "string", "format": "date" }
          }
        }
        """;

    [Fact]
    public void Validate_RepeatedSchemaId_DoesNotConflict()
    {
        var validator = new JsonSchemaValidator();
        var movie = JsonSerializer.SerializeToElement(new
        {
            title = "Movie",
            director = "Director",
            releaseDate = "2026-11-09"
        });

        Assert.Empty(validator.Validate(MovieSchema, movie));
        Assert.Empty(validator.Validate(MovieSchema, movie));
    }

    [Fact]
    public void Validate_RevisedSchemaWithSameId_UsesCurrentRules()
    {
        var validator = new JsonSchemaValidator();
        var movie = JsonSerializer.SerializeToElement(new
        {
            title = "Movie",
            director = "Director",
            releaseDate = "2026-11-09"
        });
        var revisedSchema = MovieSchema.Replace("\"director\", \"releaseDate\"",
            "\"director\", \"releaseDate\", \"genre\"");

        Assert.Empty(validator.Validate(MovieSchema, movie));
        Assert.NotEmpty(validator.Validate(revisedSchema, movie));
    }
}
