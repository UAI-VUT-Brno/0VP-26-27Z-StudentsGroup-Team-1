namespace UAI.ZeroVP.Data.Abstractions;

/// <summary>
/// Jak dopadl zápis s kontrolou verze. Hodnota <c>bool</c> by nestačila: API musí odlišit
/// "úkol neexistuje" (404) od "úkol mezitím změnil někdo jiný" (409).
/// </summary>
public enum TodoWriteStatus
{
    /// <summary>Verze souhlasila a zápis proběhl.</summary>
    Success,

    /// <summary>Úkol se zadaným identifikátorem neexistuje; nic se nezapsalo.</summary>
    NotFound,

    /// <summary>Uložená verze se liší od očekávané; nic se nezapsalo.</summary>
    Conflict
}
