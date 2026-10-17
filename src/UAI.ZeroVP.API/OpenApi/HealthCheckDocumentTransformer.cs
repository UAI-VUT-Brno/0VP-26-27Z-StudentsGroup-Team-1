using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UAI.ZeroVP.API.OpenApi;

/// <summary>
/// Doplní do dokumentu endpoint <c>/health</c>.
/// <c>MapHealthChecks</c> svůj endpoint generátoru OpenAPI nepopisuje, takže by v dokumentu chyběl.
/// </summary>
public sealed class HealthCheckDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string Path = "/health";
    private const string Tag = "Health";

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Tags ??= new HashSet<OpenApiTag>();
        document.Tags.Add(new OpenApiTag { Name = Tag });

        var operation = new OpenApiOperation
        {
            Tags = new HashSet<OpenApiTagReference> { new(Tag, document) },
            Summary = "Kontrola zdraví aplikace.",
            Description = "Volá ji HEALTHCHECK v Dockerfile. Odpovědí je stav aplikace jako prostý text.",
            Responses = new OpenApiResponses
            {
                ["200"] = TextResponse("Aplikace je zdravá, případně funguje s omezením.", "Healthy", "Degraded"),
                ["503"] = TextResponse("Aplikace není zdravá.", "Unhealthy")
            }
        };

        document.Paths.Add(Path, new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation }
        });

        return Task.CompletedTask;
    }

    /// <summary>Odpověď <c>text/plain</c>, jejímž obsahem je jeden z uvedených stavů.</summary>
    private static OpenApiResponse TextResponse(string description, params string[] statuses) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["text/plain"] = new()
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = statuses.Select(status => (JsonNode)JsonValue.Create(status)).ToList()
                }
            }
        }
    };
}
