using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using UAI.ZeroVP.DTO.Json;

namespace UAI.ZeroVP.API.OpenApi;

/// <summary>
/// Doplní typ <c>string</c> vlastnostem s převodníkem <see cref="AssumeUtcDateTimeOffsetConverter"/>.
/// U vlastního převodníku generátor nezná tvar JSON, typ vynechá a schéma by pak připouštělo cokoli.
/// </summary>
public sealed class AssumeUtcDateTimeSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Podle atributu, ne podle CustomConverter: u typu DateTimeOffset? ho System.Text.Json
        // obalí do NullableConverter a náš převodník by se tam nenašel.
        var converterAttribute = context.JsonPropertyInfo?.AttributeProvider?
            .GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .OfType<JsonConverterAttribute>()
            .FirstOrDefault();

        if (converterAttribute?.ConverterType == typeof(AssumeUtcDateTimeOffsetConverter))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}