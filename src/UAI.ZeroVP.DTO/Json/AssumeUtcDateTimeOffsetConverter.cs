using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UAI.ZeroVP.DTO.Json;

/// <summary>
/// Čte datum a čas ve formátu ISO 8601; hodnotu bez časové zóny považuje za UTC.
/// </summary>
/// <remarks>
/// Výchozí převodník System.Text.Json by hodnotu bez zóny (<c>2026-10-26T09:00:00</c>) vyložil
/// v místním čase serveru – na notebooku v Praze by z ní bylo 08:00 UTC, v kontejneru 09:00 UTC.
/// </remarks>
public sealed class AssumeUtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Formát ISO 8601 ověří System.Text.Json stejně jako u ostatních dat v API;
        // tady se jen jinak vykládá chybějící časová zóna.
        if (!reader.TryGetDateTimeOffset(out _))
        {
            throw new JsonException("Hodnota není datum a čas ve formátu ISO 8601.");
        }

        return DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}