namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Buckets that company direct costs (accounts 51, 52) are split into.
/// M3 is the complement of M1 and M2 - every department that is not
/// explicitly mapped lands there, so the three pools always sum to the
/// full ledger total for the period.
/// </summary>
public enum CostPool
{
    /// <summary>Manufacturing (VYROBA).</summary>
    M1,

    /// <summary>Warehouse and marketing (SKLAD, MARKETING).</summary>
    M2,

    /// <summary>Overhead - centrala, rezie and anything unassigned.</summary>
    M3
}
