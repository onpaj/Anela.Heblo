using Anela.Heblo.Domain.Accounting.CostPools;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// The single place that decides which cost pool a ledger department belongs to.
///
/// M1 and M2 are explicit; M3 is deliberately the catch-all so that a cost centre
/// added in Flexi later shows up in the totals instead of silently vanishing.
/// The cost of that choice is that a miscoded entry becomes overhead - CostPoolService
/// logs the distinct departments it folded into M3 so a new code is visible.
/// </summary>
public static class CostPoolDefinition
{
    /// <summary>Consumed material and services. Same prefix set as LedgerService.GetDirectCosts.</summary>
    public const string AccountPrefixMaterial = "51";

    /// <summary>Personnel costs. Same prefix set as LedgerService.GetDirectCosts.</summary>
    public const string AccountPrefixPersonnel = "52";

    public const string ManufacturingDepartment = "VYROBA";
    public const string WarehouseDepartment = "SKLAD";
    public const string MarketingDepartment = "MARKETING";

    private static readonly IReadOnlyDictionary<string, CostPool> DepartmentToPool =
        new Dictionary<string, CostPool>(StringComparer.OrdinalIgnoreCase)
        {
            [ManufacturingDepartment] = CostPool.M1,
            [WarehouseDepartment] = CostPool.M2,
            [MarketingDepartment] = CostPool.M2,
        };

    /// <summary>Every pool, in reporting order.</summary>
    public static IReadOnlyList<CostPool> All { get; } =
        new[] { CostPool.M1, CostPool.M2, CostPool.M3 };

    /// <summary>The debit account prefixes that define "direct cost".</summary>
    public static IReadOnlyList<string> AccountPrefixes { get; } =
        new[] { AccountPrefixMaterial, AccountPrefixPersonnel };

    /// <summary>
    /// Resolves a ledger department code to its pool. Unknown, null, empty and
    /// whitespace-only departments all resolve to M3.
    /// </summary>
    public static CostPool Resolve(string? department)
    {
        if (string.IsNullOrWhiteSpace(department))
        {
            return CostPool.M3;
        }

        return DepartmentToPool.TryGetValue(department, out var pool) ? pool : CostPool.M3;
    }
}
