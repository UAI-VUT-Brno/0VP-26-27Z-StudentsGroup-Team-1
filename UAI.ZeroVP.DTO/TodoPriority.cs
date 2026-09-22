namespace UAI.ZeroVP.DTO;

/// <summary>
/// Priorita úkolu. Sdílený výčet – používá ho jak API kontrakt, tak datová vrstva.
/// </summary>
public enum TodoPriority
{
    /// <summary>Nízká priorita.</summary>
    Low = 0,

    /// <summary>Běžná priorita (výchozí).</summary>
    Normal = 1,

    /// <summary>Vysoká priorita.</summary>
    High = 2
}
