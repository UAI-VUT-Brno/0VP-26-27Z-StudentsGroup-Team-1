using System.ComponentModel.DataAnnotations;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>
/// Spouští validaci podle atributů DataAnnotations stejně, jako ji v API
/// provádí ASP.NET Core díky <c>[ApiController]</c> – ale bez HTTP vrstvy.
/// </summary>
internal static class ValidationHelper
{
    /// <summary>Zvaliduje objekt a vrátí seznam nalezených chyb (prázdný = platný).</summary>
    public static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();

        // validateAllProperties: true – jinak by se kontroloval jen [Required].
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        return results;
    }

    /// <summary>Ověří, že validace selhala právě na zadané vlastnosti.</summary>
    public static void AssertSingleErrorFor(object model, string memberName)
    {
        var error = Assert.Single(Validate(model));
        Assert.Contains(memberName, error.MemberNames);
    }
}
