### task: remove-interface-member

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs:27-30`

- [ ] **Step 1: Delete the `GetCacheStatus()` declaration and its XML doc comment from the interface**

Current content of `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs`:

```csharp
using Anela.Heblo.Application.Features.FinancialOverview;

namespace Anela.Heblo.Application.Features.FinancialOverview.Services;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Gets financial overview data, preferably from cache.
    /// When <paramref name="excludedDepartments"/> is null or empty and <paramref name="includeCurrentMonth"/> is false,
    /// the cached path is used. Otherwise, a real-time calculation is performed.
    /// </summary>
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
        bool includeCurrentMonth = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes cached financial data for specified date range
    /// </summary>
    Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cache status for monitoring
    /// </summary>
    FinancialAnalysisCacheStatus GetCacheStatus();

    /// <summary>
    /// Gets year-over-year financial comparison data, aligning each year's partial month
    /// to the same cutoff day for a fair comparison.
    /// </summary>
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default);
}
```

Delete these four lines (the `GetCacheStatus()` XML doc comment and declaration), leaving one blank line between `RefreshFinancialDataAsync` and `GetFinancialComparisonAsync`:

```csharp
    /// <summary>
    /// Gets the cache status for monitoring
    /// </summary>
    FinancialAnalysisCacheStatus GetCacheStatus();

```

Use the Edit tool with this exact old/new pair (the surrounding methods are unique anchors, so the block is safe to target directly):

Old:
```csharp
    Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cache status for monitoring
    /// </summary>
    FinancialAnalysisCacheStatus GetCacheStatus();

    /// <summary>
    /// Gets year-over-year financial comparison data, aligning each year's partial month
```

New:
```csharp
    Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets year-over-year financial comparison data, aligning each year's partial month
```

The resulting file must read exactly:

```csharp
using Anela.Heblo.Application.Features.FinancialOverview;

namespace Anela.Heblo.Application.Features.FinancialOverview.Services;

public interface IFinancialAnalysisService
{
    /// <summary>
    /// Gets financial overview data, preferably from cache.
    /// When <paramref name="excludedDepartments"/> is null or empty and <paramref name="includeCurrentMonth"/> is false,
    /// the cached path is used. Otherwise, a real-time calculation is performed.
    /// </summary>
    Task<GetFinancialOverviewResponse> GetFinancialOverviewAsync(
        int months,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments = null,
        bool includeCurrentMonth = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes cached financial data for specified date range
    /// </summary>
    Task RefreshFinancialDataAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets year-over-year financial comparison data, aligning each year's partial month
    /// to the same cutoff day for a fair comparison.
    /// </summary>
    Task<GetFinancialComparisonResponse> GetFinancialComparisonAsync(
        int years,
        bool includeStockData,
        IReadOnlyList<string>? excludedDepartments,
        bool includePartialMonth,
        CancellationToken cancellationToken = default);
}
```

Note: do not touch the `FinancialAnalysisCacheStatus` type or its file — it stays as-is, unused by this interface but still the private method's return type.

- [ ] **Step 2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/IFinancialAnalysisService.cs
git commit -m "Remove unused GetCacheStatus() from IFinancialAnalysisService

GetCacheStatus() has no caller through the interface (only internal
self-calls on 'this' from within FinancialAnalysisService.GetFinancialOverviewAsync)
and is not exposed via any controller or background task. Removing it
narrows the interface to only the operations real callers use.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Sa8QWrDhReHjVbCGP9jWGT"
```

---
