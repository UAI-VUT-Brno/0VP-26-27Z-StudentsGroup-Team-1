using System.Globalization;

namespace UAI.ZeroVP.API.Instance;

/// <summary>
/// Jméno běžící instance API. Když běží několik instancí nad stejnými daty,
/// podle něj se pozná, která požadavek obsloužila – v <c>GET /api/info</c>,
/// v hlavičce <see cref="HeaderName"/> každé odpovědi a v každém řádku logu.
/// </summary>
public sealed class InstanceIdentity
{
    /// <summary>Název sekce v <c>appsettings.json</c>.</summary>
    public const string SectionName = "Instance";

    /// <summary>Hlavička odpovědi se jménem instance.</summary>
    public const string HeaderName = "X-Instance-Id";

    /// <summary>Vytvoří identitu se zadaným jménem.</summary>
    /// <param name="name">Jméno instance; nesmí být prázdné.</param>
    public InstanceIdentity(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    /// <summary>Jméno instance, např. <c>api-5001</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Přečte jméno z nastavení <c>Instance:Name</c> (proměnná prostředí <c>Instance__Name</c>).
    /// Když chybí, složí se ze jména stroje a čísla procesu – to se liší i u instancí
    /// spuštěných na jednom počítači, a v kontejneru je jméno stroje ID kontejneru.
    /// </summary>
    public static InstanceIdentity FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[$"{SectionName}:Name"];

        return new InstanceIdentity(string.IsNullOrWhiteSpace(configured)
            ? string.Create(CultureInfo.InvariantCulture, $"{Environment.MachineName}-{Environment.ProcessId}")
            : configured);
    }
}