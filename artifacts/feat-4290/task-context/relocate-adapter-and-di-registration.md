### task: relocate-adapter-and-di-registration

**Files:**
- Move: `backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs` → `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs`

- [ ] **Step 1: Move the file with `git mv` (preserves history)**

Run:
```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
git mv backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs
```
Expected: `git status` shows the file staged as a rename (`renamed: backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs -> backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs`).

- [ ] **Step 2: Update the namespace and add the cross-module adapter comment in the moved file**

Replace the full contents of `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs` with:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Invoices;

// Cross-module contract: Invoices implements MarketingPerformance's IMonthlyRevenueSource
// via this adapter. DI registration owned by provider (Invoices), not consumer
// (MarketingPerformance) — mirrors the IInvoiceConsumptionSource/IInvoiceImportStatisticsSource
// pattern in InvoicesModule.cs.
/// <summary>
/// Revenue step: aggregates the already-synced Shoptet issued invoices for one month.
/// CZK only, by TaxDate. Price is the with-VAT total (PriceC is never populated).
/// Wholesale = customer has a VAT ID (VatPayer == true) — the same rule Flexi sales query 37 uses.
/// </summary>
public class IssuedInvoiceMonthlyRevenueSource : IMonthlyRevenueSource
{
    private const string Czk = "CZK";
    private const string Eur = "EUR";
    private readonly ApplicationDbContext _context;

    public IssuedInvoiceMonthlyRevenueSource(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var start = month.Start;
        var end = month.EndExclusive;

        var groups = await _context.IssuedInvoices.AsNoTracking()
            .Where(i => i.Currency == Czk && i.TaxDate >= start && i.TaxDate < end)
            .GroupBy(i => i.VatPayer == true)
            .Select(g => new { IsWholesale = g.Key, Count = g.Count(), Sum = g.Sum(i => i.Price) })
            .ToListAsync(cancellationToken);

        var eurCount = await _context.IssuedInvoices.AsNoTracking()
            .CountAsync(i => i.Currency == Eur && i.TaxDate >= start && i.TaxDate < end, cancellationToken);

        var retail = groups.SingleOrDefault(g => !g.IsWholesale);
        var wholesale = groups.SingleOrDefault(g => g.IsWholesale);

        return new MonthlyRevenueSnapshot
        {
            RetailOrderCount = retail?.Count ?? 0,
            RetailRevenueWithVat = retail?.Sum ?? 0m,
            WholesaleOrderCount = wholesale?.Count ?? 0,
            WholesaleRevenueWithVat = wholesale?.Sum ?? 0m,
            SkippedEurInvoiceCount = eurCount,
        };
    }
}
```

The only changes from the original file are: line 4 (`namespace Anela.Heblo.Persistence.Marketing;` → `namespace Anela.Heblo.Persistence.Invoices;`) and the added `//` comment block above the class. The class body (constructor, `GetAsync`, private constants, XML doc) is byte-for-byte identical to the original.

Expected: file saved; `git diff --staged` (after Step 4's `git add`) shows only the namespace line and the new comment block as additions to this file, nothing else.

- [ ] **Step 3: Remove the registration and now-unused `using` from `MarketingPerformanceModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs`, remove the `using Anela.Heblo.Persistence.Marketing;` line (line 5) and the `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` line (line 22).

Use these two edits:

Edit 1 — remove the using:
```diff
 using Anela.Heblo.Domain.Features.MarketingPerformance;
-using Anela.Heblo.Persistence.Marketing;
 using Microsoft.Extensions.Configuration;
```

Edit 2 — remove the registration:
```diff
         services.AddScoped<IMarketingPerformanceRepository, MarketingPerformanceRepository>();
-        services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
         // No-op fallback; the Flexi adapter will override with the real received-invoice
         // implementation when it is registered (last registration wins).
         services.AddScoped<IMonthlyAdCostSource, NoOpMonthlyAdCostSource>();
```

The resulting file must read exactly:

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance;

public static class MarketingPerformanceModule
{
    public static IServiceCollection AddMarketingPerformanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketingPerformanceOptions>()
            .Bind(configuration.GetSection(MarketingPerformanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MarketingPerformanceOptions>, MarketingPerformanceOptionsValidator>();

        services.AddScoped<IMarketingPerformanceRepository, MarketingPerformanceRepository>();
        // No-op fallback; the Flexi adapter will override with the real received-invoice
        // implementation when it is registered (last registration wins).
        services.AddScoped<IMonthlyAdCostSource, NoOpMonthlyAdCostSource>();
        services.AddSingleton<MarketingPerformanceRunGuard>();
        services.AddScoped<IMarketingPerformanceRefreshService, MarketingPerformanceRefreshService>();
        services.AddScoped<MarketingPerformanceRecomputeJob>();
        services.AddScoped<IMarketingPerformanceRecomputeEnqueuer, HangfireMarketingPerformanceRecomputeEnqueuer>();
        // MediatR handlers and IRecurringJob implementations are discovered by assembly scan.
        return services;
    }
}
```

(`using Anela.Heblo.Domain.Features.MarketingPerformance;` stays — it is still required for `IMarketingPerformanceRepository`, `IMonthlyAdCostSource`, etc., which remain in that namespace.)

Expected: file matches exactly; no other lines changed.

- [ ] **Step 4: Add the registration to `InvoicesModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`, add `using Anela.Heblo.Domain.Features.MarketingPerformance;` to the usings, and add the new registration as the third cross-module adapter entry, right after the existing `IInvoiceImportStatisticsSource` registration and before the DataQuality adapters' comment block.

Edit 1 — add the using (after the existing `Anela.Heblo.Domain.Features.Invoices` using):
```diff
 using Anela.Heblo.Domain.Features.Invoices;
+using Anela.Heblo.Domain.Features.MarketingPerformance;
 using Anela.Heblo.Persistence.Invoices;
```

Edit 2 — add the registration:
```diff
         services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();

+        // Cross-module contract: Invoices implements MarketingPerformance's IMonthlyRevenueSource
+        // via an adapter. DI registration owned by provider (Invoices), not consumer
+        // (MarketingPerformance) — mirrors the IInvoiceConsumptionSource/IInvoiceImportStatisticsSource
+        // pattern above.
+        services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
+
         // Cross-module contracts: Invoices implements DataQuality's IInvoiceShoptetSource
         // and IInvoiceErpClient via adapters. Lifetimes mirror the wrapped services exactly:
```

The resulting top-of-file usings block and the registration section must read exactly:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence.Invoices;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;
```

```csharp
        // Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (PackingMaterials) — keeps the dependency direction inverted properly.
        services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();

        // Cross-module contract: Invoices implements Analytics' IInvoiceImportStatisticsSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (Analytics) — mirrors the IInvoiceConsumptionSource pattern above. Scoped because
        // the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();

        // Cross-module contract: Invoices implements MarketingPerformance's IMonthlyRevenueSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (MarketingPerformance) — mirrors the IInvoiceConsumptionSource/IInvoiceImportStatisticsSource
        // pattern above.
        services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();

        // Cross-module contracts: Invoices implements DataQuality's IInvoiceShoptetSource
        // and IInvoiceErpClient via adapters. Lifetimes mirror the wrapped services exactly:
        //   - IIssuedInvoiceSource is registered Singleton in Program.cs:119, so the adapter
        //     must also be Singleton (and DataQuality consumers must resolve it from a Scoped
        //     scope as usual — Singleton from Scoped is legal, the inverse is captive).
        //   - IIssuedInvoiceClient is registered Scoped in FlexiAdapterServiceCollectionExtensions.cs:93,
        //     so the adapter must also be Scoped.
        services.AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>();
        services.AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>();
```

Everything else in the file (repository registration, transformation registrations, closing brace) is unchanged.

Expected: file compiles; `IMonthlyRevenueSource` and `IssuedInvoiceMonthlyRevenueSource` both resolve (the former from the new `using Anela.Heblo.Domain.Features.MarketingPerformance;`, the latter from the pre-existing `using Anela.Heblo.Persistence.Invoices;`).

- [ ] **Step 5: Fix the test file's `using` (required for `dotnet build` — arch-review Specification Amendment 2)**

In `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs`, change:
```diff
 using Anela.Heblo.Domain.Features.Invoices;
 using Anela.Heblo.Domain.Features.MarketingPerformance;
 using Anela.Heblo.Persistence;
-using Anela.Heblo.Persistence.Marketing;
+using Anela.Heblo.Persistence.Invoices;
 using FluentAssertions;
```
No other change to this file — the test body, class name, and namespace (`Anela.Heblo.Tests.Features.MarketingPerformance`) stay as-is per arch-review Specification Amendment 2 (relocating the test file itself is explicitly optional and not required).

Expected: file's usings match the diff above; nothing else changed.

- [ ] **Step 6: Build and run the affected tests to verify the move compiles and behaves identically**

Run:
```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
dotnet build
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings introduced by the move — FR-1 acceptance criterion).

Run:
```bash
dotnet test --filter "FullyQualifiedName~IssuedInvoiceMonthlyRevenueSourceTests|FullyQualifiedName~MarketingPerformanceRefreshServiceTests"
```
Expected: all tests in both fixtures pass — `IssuedInvoiceMonthlyRevenueSourceTests.GetAsync_SplitsRetailAndWholesale_ByTaxDate_CzkOnly`, `IssuedInvoiceMonthlyRevenueSourceTests.GetAsync_EmptyMonth_ReturnsZeros`, and every test in `MarketingPerformanceRefreshServiceTests` (confirms `IMonthlyRevenueSource` still resolves correctly for `MarketingPerformanceRefreshService`, satisfying FR-2's last acceptance criterion). `Passed!` with 0 failed, 0 skipped for the filtered set.

- [ ] **Step 7: Commit**

```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
git add backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs
git commit -m "$(cat <<'EOF'
Move IssuedInvoiceMonthlyRevenueSource to Invoices module (fix #4290)

IssuedInvoiceMonthlyRevenueSource implements MarketingPerformance's
IMonthlyRevenueSource contract by querying ApplicationDbContext.IssuedInvoices
directly, but lived in the consumer module (MarketingPerformance) instead of
the provider module (Invoices), which owns the IssuedInvoice entity — a
"Direct access to another module's entities" violation of
development_guidelines.md.

Relocates the adapter to Anela.Heblo.Persistence.Invoices and its DI
registration to InvoicesModule, mirroring the existing
IInvoiceConsumptionSource/IInvoiceImportStatisticsSource provider-owns-adapter
entries already in InvoicesModule.cs. Pure move: no behavior, query, or
contract change.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01RC4VFf829MtzjZ7v5DP5ny
EOF
)"
```
Expected: commit succeeds; `git status` shows a clean working tree with one new commit.

---

