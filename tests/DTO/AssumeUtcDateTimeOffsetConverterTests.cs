using System.Text.Json;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>
/// Čtení <c>firstDueAtUtc</c> z JSON. Výsledek nesmí záviset na časové zóně serveru:
/// stejný požadavek musí dát stejný termín na notebooku v Praze i v kontejneru v UTC.
/// </summary>
public sealed class AssumeUtcDateTimeOffsetConverterTests
{
    private static readonly DateTimeOffset Expected = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Stejné nastavení jako ve webovém API (camelCase názvy vlastností).</summary>
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static DateTimeOffset? ReadFirstDue(string jsonValue) =>
        JsonSerializer.Deserialize<CreateRepeatingTodoItemRequest>(
            $$"""{"title":"Úkol","repeatCount":1,"firstDueAtUtc":{{jsonValue}}}""", WebOptions)!.FirstDueAtUtc;

    [Theory]
    [InlineData("\"2026-10-26T09:00:00Z\"")]
    [InlineData("\"2026-10-26T09:00:00+00:00\"")]
    [InlineData("\"2026-10-26T11:00:00+02:00\"")]
    [InlineData("\"2026-10-26T04:00:00-05:00\"")]
    public void Read_ValueWithOffset_KeepsTheInstant(string jsonValue)
    {
        Assert.Equal(Expected, ReadFirstDue(jsonValue));
    }

    [Fact]
    public void Read_ValueWithoutOffset_IsTreatedAsUtc()
    {
        // Výchozí převodník by tady dosadil místní posun serveru.
        var value = ReadFirstDue("\"2026-10-26T09:00:00\"");

        Assert.Equal(Expected, value);
        Assert.Equal(TimeSpan.Zero, value!.Value.Offset);
    }

    [Fact]
    public void Read_DateOnly_IsMidnightUtc()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), ReadFirstDue("\"2026-10-26\""));
    }

    [Fact]
    public void Read_Null_StaysNull()
    {
        // Chybějící termín pak odmítne [Required] se srozumitelnou hláškou.
        Assert.Null(ReadFirstDue("null"));
    }

    [Theory]
    [InlineData("\"zítra\"")]
    [InlineData("\"26.10.2026\"")]
    [InlineData("\"10/26/2026\"")]
    [InlineData("\"\"")]
    [InlineData("123")]
    public void Read_NotIso8601_Throws(string jsonValue)
    {
        Assert.Throws<JsonException>(() => ReadFirstDue(jsonValue));
    }

    [Fact]
    public void Write_ProducesIso8601()
    {
        var request = new CreateRepeatingTodoItemRequest { Title = "Úkol", FirstDueAtUtc = Expected, RepeatCount = 1 };

        var json = JsonSerializer.Serialize(request, WebOptions);

        Assert.Contains("\"firstDueAtUtc\":\"2026-10-26T09:00:00+00:00\"", json);
    }
}