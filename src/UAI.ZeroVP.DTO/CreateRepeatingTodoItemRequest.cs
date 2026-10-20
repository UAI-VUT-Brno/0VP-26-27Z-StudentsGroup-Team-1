using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace UAI.ZeroVP.DTO;

/// <summary>
/// Vstup pro založení týdně se opakujícího úkolu (HTTP POST /api/todos/repeat).
/// Vznikne první výskyt a <see cref="RepeatCount"/> dalších, vždy o sedm dní později.
/// Každý výskyt je samostatný úkol – nejde o trvalé pravidlo opakování.
/// </summary>
/// <remarks>
/// Pravidla pro název, popis a prioritu musí odpovídat <see cref="CreateTodoItemRequest"/>.
/// </remarks>
public sealed record CreateRepeatingTodoItemRequest : IValidatableObject
{
    /// <summary>Nejmenší povolený počet opakování.</summary>
    public const int MinRepeatCount = 1;

    /// <summary>Největší povolený počet opakování – jeden rok dopředu.</summary>
    public const int MaxRepeatCount = 52;

    /// <summary>Počet dní mezi dvěma výskyty.</summary>
    public const int DaysBetweenOccurrences = 7;

    /// <summary>Krátký název úkolu. Povinný, 1–200 znaků.</summary>
    /// <remarks>
    /// Bez klíčového slova <c>required</c> ze stejného důvodu jako
    /// u <see cref="CreateTodoItemRequest.Title"/>.
    /// </remarks>
    /// <example>Týdenní report</example>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Název úkolu je povinný.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Název musí mít 1 až 200 znaků.")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Volitelný popis, maximálně 1000 znaků.</summary>
    /// <example>Shrnout, co se za týden udělalo.</example>
    [StringLength(1000, ErrorMessage = "Popis smí mít nejvýše 1000 znaků.")]
    public string? Description { get; init; }

    /// <summary>Priorita úkolu. Výchozí hodnota je <c>Normal</c>.</summary>
    [DefaultValue(TodoPriority.Normal)]
    [EnumDataType(typeof(TodoPriority), ErrorMessage = "Neplatná hodnota priority.")]
    public TodoPriority Priority { get; init; } = TodoPriority.Normal;

    /// <summary>Termín prvního výskytu v UTC, ve formátu ISO 8601 (např. <c>2026-10-26T09:00:00Z</c>). Povinný.</summary>
    /// <remarks>
    /// Typ s <c>?</c>, aby šlo poznat chybějící hodnotu: jinak by se dosadilo
    /// <c>0001-01-01</c> a <c>[Required]</c> by nic nenašel.
    /// </remarks>
    /// <example>2026-10-26T09:00:00Z</example>
    [Required(ErrorMessage = "Termín prvního výskytu je povinný.")]
    public DateTimeOffset? FirstDueAtUtc { get; init; }

    /// <summary>Počet dalších týdenních opakování po prvním výskytu, 1–52. Hodnota 3 vytvoří čtyři úkoly.</summary>
    /// <example>3</example>
    [Range(MinRepeatCount, MaxRepeatCount, ErrorMessage = "Počet opakování musí být 1 až 52.")]
    public int RepeatCount { get; init; }

    /// <summary>
    /// Kontrola, kterou nejde vyjádřit atributem: poslední výskyt musí být platné datum.
    /// ASP.NET Core ji volá, až když projdou atributy jednotlivých vlastností.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FirstDueAtUtc is not { } first || RepeatCount is < MinRepeatCount or > MaxRepeatCount)
        {
            yield break;
        }

        var lastOccurrenceOffset = TimeSpan.FromDays(DaysBetweenOccurrences * RepeatCount);
        if (first.UtcDateTime > DateTime.MaxValue - lastOccurrenceOffset)
        {
            yield return new ValidationResult(
                "Poslední výskyt by připadl za rok 9999.",
                [nameof(FirstDueAtUtc)]);
        }
    }
}