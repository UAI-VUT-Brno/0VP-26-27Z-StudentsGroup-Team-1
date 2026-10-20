using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.API.Scheduling;

/// <summary>
/// Výpočet termínů týdně se opakujícího úkolu.
/// Počítá se v UTC, takže mezi výskyty je vždy přesně 7 × 24 hodin –
/// přechod na letní a zimní čas termíny neposune.
/// </summary>
public static class WeeklySchedule
{
    /// <summary>
    /// Vrátí termín prvního výskytu a <paramref name="repeatCount"/> dalších, vždy o týden později.
    /// </summary>
    /// <param name="firstDueAtUtc">Termín prvního výskytu; jiný posun než UTC se převede na UTC.</param>
    /// <param name="repeatCount">Počet dalších opakování po prvním výskytu.</param>
    /// <returns>Seznam o <c>repeatCount + 1</c> termínech seřazený od nejbližšího.</returns>
    public static IReadOnlyList<DateTimeOffset> GetDueDates(DateTimeOffset firstDueAtUtc, int repeatCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(repeatCount);

        var first = firstDueAtUtc.ToUniversalTime();

        return Enumerable.Range(0, repeatCount + 1)
            .Select(week => first.AddDays(week * CreateRepeatingTodoItemRequest.DaysBetweenOccurrences))
            .ToList();
    }
}