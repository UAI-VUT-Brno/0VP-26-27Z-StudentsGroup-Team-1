using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace UAI.ZeroVP.DTO;

/// <summary>
/// Vstup pro vytvoření nového úkolu (HTTP POST /api/todos).
/// Validační atributy vyhodnocuje ASP.NET Core automaticky díky <c>[ApiController]</c>.
/// </summary>
public sealed record CreateTodoItemRequest
{
    /// <summary>Krátký název úkolu. Povinný, 1–200 znaků.</summary>
    /// <remarks>
    /// Záměrně bez klíčového slova <c>required</c>: to vynucuje už deserializace JSON,
    /// takže úplně chybějící pole by skončilo chybou deserializace a hláška
    /// z <c>[Required]</c> by se ke klientovi nikdy nedostala.
    /// </remarks>
    /// <example>Napsat Dockerfile</example>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Název úkolu je povinný.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Název musí mít 1 až 200 znaků.")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Volitelný popis, maximálně 1000 znaků.</summary>
    /// <example>Vícefázový build, výsledný image do 400 MB.</example>
    [StringLength(1000, ErrorMessage = "Popis smí mít nejvýše 1000 znaků.")]
    public string? Description { get; init; }

    /// <summary>Priorita úkolu. Výchozí hodnota je <c>Normal</c>.</summary>
    [DefaultValue(TodoPriority.Normal)]
    [EnumDataType(typeof(TodoPriority), ErrorMessage = "Neplatná hodnota priority.")]
    public TodoPriority Priority { get; init; } = TodoPriority.Normal;
}
