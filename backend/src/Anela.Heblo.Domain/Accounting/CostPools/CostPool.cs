namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Buckets that company direct costs are split into. The account set is
/// per pool, not global: M2 counts 50, 51 and 52, the others 51 and 52.
///
/// M3 is the complement of M1 and M2 - every department that is not
/// explicitly mapped lands there, so a cost centre added in Flexi shows up
/// instead of vanishing.
///
/// The pools do NOT sum to the full ledger total: BUVOL is excluded as a
/// separate activity, and 50x outside SKLAD/MARKETING is cost of goods sold,
/// which would dwarf every pool combined. Use CostPoolDefinition.Resolve to
/// bucket an entry rather than assuming it lands somewhere.
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
