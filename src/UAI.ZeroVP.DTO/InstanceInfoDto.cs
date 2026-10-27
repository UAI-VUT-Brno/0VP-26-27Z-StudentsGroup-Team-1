namespace UAI.ZeroVP.DTO;

/// <summary>
/// Informace o běžící instanci API.
/// Když běží několik instancí (replik kontejneru) nad stejnými daty,
/// je podle něj vidět, která požadavek obsloužila.</summary>
public sealed record InstanceInfoDto
{
    /// <summary>Jméno instance z nastavení <c>Instance:Name</c>; stejné jako hlavička <c>X-Instance-Id</c>.</summary>
    public required string InstanceName { get; init; }

    /// <summary>Jméno stroje – v kontejneru odpovídá jeho hostname (zkrácené ID kontejneru).</summary>
    public required string MachineName { get; init; }

    /// <summary>Název prostředí ASP.NET Core (Development / Production / ...).</summary>
    public required string Environment { get; init; }

    /// <summary>Verze sestavení API.</summary>
    public required string Version { get; init; }

    /// <summary>Aktuální čas instance v UTC.</summary>
    public required DateTimeOffset UtcNow { get; init; }

    /// <summary>Doba běhu procesu – po restartu kontejneru se vynuluje.</summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>Počet úkolů v úložišti – u sdíleného souboru stejný pro všechny instance.</summary>
    public required int TodoCount { get; init; }
}
