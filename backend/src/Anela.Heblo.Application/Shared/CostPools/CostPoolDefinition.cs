using Anela.Heblo.Domain.Accounting.CostPools;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// The single place that decides whether a ledger entry belongs to a cost pool,
/// and to which one. Both the department and the debit account matter, because
/// the same account prefix means different things in different cost centres.
///
/// M1 and M2 are explicit; M3 is deliberately the catch-all so that a cost centre
/// added in Flexi later shows up in the totals instead of silently vanishing.
/// The cost of that choice is that a miscoded entry becomes overhead - CostPoolService
/// logs the distinct departments it folded into M3 so a new code is visible.
/// </summary>
public static class CostPoolDefinition
{
    /// <summary>
    /// Consumed goods and material. Counts only inside M2, where it is shipping
    /// packaging (cartons, printed tape) and marketing print - real fulfilment and
    /// marketing spend. In centrala the same prefix is cost of goods sold, which
    /// dwarfs every pool combined and must not leak into the M3 catch-all.
    /// </summary>
    public const string AccountPrefixConsumables = "50";

    /// <summary>Consumed services.</summary>
    public const string AccountPrefixMaterial = "51";

    /// <summary>Personnel costs.</summary>
    public const string AccountPrefixPersonnel = "52";

    public const string ManufacturingDepartment = "VYROBA";
    public const string WarehouseDepartment = "SKLAD";
    public const string MarketingDepartment = "MARKETING";

    /// <summary>
    /// A separate activity that shares the ledger but is not Anela overhead, so it
    /// must not be absorbed by the M3 catch-all the way an unmapped cost centre is.
    /// </summary>
    public const string SeparateActivityDepartment = "BUVOL";

    private static readonly IReadOnlyDictionary<string, CostPool> DepartmentToPool =
        new Dictionary<string, CostPool>(StringComparer.OrdinalIgnoreCase)
        {
            [ManufacturingDepartment] = CostPool.M1,
            [WarehouseDepartment] = CostPool.M2,
            [MarketingDepartment] = CostPool.M2,
        };

    private static readonly IReadOnlySet<string> ExcludedDepartments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SeparateActivityDepartment };

    private static readonly IReadOnlyList<string> PoolPrefixes =
        new[] { AccountPrefixMaterial, AccountPrefixPersonnel };

    private static readonly IReadOnlyList<string> M2Prefixes =
        new[] { AccountPrefixConsumables, AccountPrefixMaterial, AccountPrefixPersonnel };

    /// <summary>Every pool, in reporting order.</summary>
    public static IReadOnlyList<CostPool> All { get; } =
        new[] { CostPool.M1, CostPool.M2, CostPool.M3 };

    /// <summary>
    /// Every debit account prefix any pool can use - what one unfiltered ledger pull
    /// has to ask for. Wider than what any single pool counts, so a caller reading
    /// this must still bucket with <see cref="Resolve"/> rather than sum blindly.
    /// </summary>
    public static IReadOnlyList<string> AccountPrefixes { get; } = M2Prefixes;

    /// <summary>
    /// The debit account prefixes that count towards one pool. Used by the cost
    /// providers, which pull per department and so can filter server-side.
    /// </summary>
    public static IReadOnlyList<string> AccountPrefixesFor(CostPool pool) =>
        pool == CostPool.M2 ? M2Prefixes : PoolPrefixes;

    /// <summary>
    /// Resolves a ledger entry to its pool, or null when it belongs to none.
    ///
    /// Unknown, null, empty and whitespace-only departments resolve to M3.
    /// Returns null for an excluded department, and for an account prefix that
    /// pool does not count.
    /// </summary>
    public static CostPool? Resolve(string? department, string? debitAccountNumber)
    {
        if (!string.IsNullOrWhiteSpace(department) && ExcludedDepartments.Contains(department))
        {
            return null;
        }

        var pool = string.IsNullOrWhiteSpace(department)
            ? CostPool.M3
            : DepartmentToPool.TryGetValue(department, out var mapped) ? mapped : CostPool.M3;

        return CountsTowards(pool, debitAccountNumber) ? pool : null;
    }

    private static bool CountsTowards(CostPool pool, string? debitAccountNumber) =>
        debitAccountNumber is not null
        && AccountPrefixesFor(pool).Any(prefix => debitAccountNumber.StartsWith(prefix, StringComparison.Ordinal));
}
