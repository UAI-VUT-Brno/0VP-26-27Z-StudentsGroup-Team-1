using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UAI.ZeroVP.API.OpenApi;

/// <summary>
/// Upraví popis odpovědí tak, aby odpovídal tomu, co API skutečně posílá.
/// Generátor OpenAPI vychází jen z atributů kontroleru a tyto detaily z nich nepozná.
/// </summary>
public sealed class ResponsesOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.Responses is not { Count: > 0 } responses)
        {
            return Task.CompletedTask;
        }

        foreach (var (statusCode, response) in responses)
        {
            if (response is not OpenApiResponse concreteResponse)
            {
                continue;
            }

            RemoveMediaTypesWithoutSchema(concreteResponse);

            if (statusCode == "201")
            {
                AddLocationHeader(concreteResponse);
            }
        }

        SortByStatusCode(responses);

        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>[Produces("application/json")]</c> na kontroleru přidá typ <c>application/json</c> ke každé odpovědi,
    /// i k odpovědi bez těla (204) nebo s jiným typem obsahu (500 posílá <c>application/problem+json</c>).
    /// Takové položky nemají schéma; zůstanou jen typy obsahu, které API opravdu vrací.
    /// </summary>
    private static void RemoveMediaTypesWithoutSchema(OpenApiResponse response)
    {
        if (response.Content is null)
        {
            return;
        }

        foreach (var mediaType in response.Content.Where(pair => pair.Value.Schema is null).ToList())
        {
            response.Content.Remove(mediaType.Key);
        }

        if (response.Content.Count == 0)
        {
            response.Content = null;
        }
    }

    /// <summary><c>CreatedAtRoute</c> posílá v odpovědi 201 adresu nově vytvořeného zdroje.</summary>
    private static void AddLocationHeader(OpenApiResponse response)
    {
        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers["Location"] = new OpenApiHeader
        {
            Description = "Adresa nově vytvořeného zdroje.",
            Required = true,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" }
        };
    }

    /// <summary>Odpovědi zadané na úrovni kontroleru (500) by jinak byly v dokumentu první.</summary>
    private static void SortByStatusCode(OpenApiResponses responses)
    {
        var sorted = responses.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        responses.Clear();
        foreach (var (statusCode, response) in sorted)
        {
            responses.Add(statusCode, response);
        }
    }
}
