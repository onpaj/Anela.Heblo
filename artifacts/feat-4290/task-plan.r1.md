# Fix MarketingPerformance → Invoices Module-Boundary Violation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Relocate `IssuedInvoiceMonthlyRevenueSource` (the `IMonthlyRevenueSource` adapter) from `Anela.Heblo.Persistence.Marketing` to `Anela.Heblo.Persistence.Invoices`, move its DI registration from `MarketingPerformanceModule` to `InvoicesModule` (provider-owns-adapter pattern, mirroring the existing `IInvoiceConsumptionSource`/`IInvoiceImportStatisticsSource` entries in `InvoicesModule.cs`), and add a `MarketingPerformance -> Invoices` architecture-boundary regression guard to `ModuleBoundariesTests.cs`. Zero behavior change; zero public API/contract change.

**Architecture:** Pure move + namespace rename of one class, a one-line DI-registration relocation between two module registration methods, and two new `ModuleBoundaryRule` entries (Application + Domain, per the existing `Analytics (Application/Domain) -> Invoices` precedent — `ModuleBoundaryRule` supports exactly one `InspectedNamespacePrefix`/`InspectedAssembly` per rule, so FR-3's three-prefix wording is implemented as two rules per arch-review Decision 2 / Specification Amendment 1). No interfaces, signatures, or query logic change. `git mv` preserves file history.

**Tech Stack:** .NET 8 / C#, xUnit, FluentAssertions, EF Core (InMemory provider for tests).

---

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

### task: add-marketingperformance-invoices-boundary-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Depends on:** `relocate-adapter-and-di-registration` (this task's rules must pass against the fix already applied, and its revert-verification step reads the previous task's commit).

- [ ] **Step 1: Add the `MarketingPerformanceInvoicesAllowlist` field**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, add a new empty allowlist field directly after `DataQualityInvoicesAllowlist` (after line 144, before the `// Allowlist for Manufacture -> Catalog.` comment):

```diff
     // Allowlist for DataQuality -> Invoices. Empty — IInvoiceShoptetSource/IInvoiceErpClient now
     // expose DataQuality-owned DqtInvoiceSnapshot/DqtInvoiceItem/DqtInvoiceSourceQuery types;
     // InvoiceShoptetSourceAdapter/InvoiceErpClientAdapter (Invoices.Infrastructure) map from
     // Invoices domain types to the DataQuality shape via InvoiceDqtSnapshotMapper.
     private static readonly HashSet<string> DataQualityInvoicesAllowlist = new(StringComparer.Ordinal);

+    // Allowlist for MarketingPerformance -> Invoices. Empty — IssuedInvoiceMonthlyRevenueSource
+    // was relocated to Anela.Heblo.Persistence.Invoices (feat-4290), closing the compile-time
+    // dependency. MarketingPerformance's own Application/Domain namespaces reference only the
+    // IMonthlyRevenueSource contract, which MarketingPerformance itself owns.
+    private static readonly HashSet<string> MarketingPerformanceInvoicesAllowlist = new(StringComparer.Ordinal);
+
     // Allowlist for Manufacture -> Catalog. Each group below is a deliberate pragmatic leak
```

Expected: field added; file still compiles (no test run needed yet — this alone doesn't touch `Rules()`).

- [ ] **Step 2: Add the two `ModuleBoundaryRule` entries**

In the same file, in the `Rules()` method, add two new entries directly after the `"DataQuality -> Invoices"` entry and before `"Manufacture -> Catalog"`:

```diff
         new ModuleBoundaryRule(
             Name: "DataQuality -> Invoices",
             InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
             ForbiddenNamespacePrefixes: new[]
             {
                 "Anela.Heblo.Domain.Features.Invoices",
                 "Anela.Heblo.Application.Features.Invoices",
                 "Anela.Heblo.Persistence.Invoices",
             },
             Allowlist: DataQualityInvoicesAllowlist),

+        new ModuleBoundaryRule(
+            Name: "MarketingPerformance (Application) -> Invoices",
+            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.MarketingPerformance",
+            ForbiddenNamespacePrefixes: new[]
+            {
+                "Anela.Heblo.Domain.Features.Invoices",
+                "Anela.Heblo.Application.Features.Invoices",
+                "Anela.Heblo.Persistence.Invoices",
+            },
+            Allowlist: MarketingPerformanceInvoicesAllowlist),
+
+        new ModuleBoundaryRule(
+            Name: "MarketingPerformance (Domain) -> Invoices",
+            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.MarketingPerformance",
+            ForbiddenNamespacePrefixes: new[]
+            {
+                "Anela.Heblo.Domain.Features.Invoices",
+                "Anela.Heblo.Application.Features.Invoices",
+                "Anela.Heblo.Persistence.Invoices",
+            },
+            Allowlist: MarketingPerformanceInvoicesAllowlist,
+            InspectedAssembly: "Anela.Heblo.Domain"),
+
         new ModuleBoundaryRule(
             Name: "Manufacture -> Catalog",
```

(Both new rules deliberately share the single `MarketingPerformanceInvoicesAllowlist` field per design.r1.md §4, unlike the `Analytics (Application/Domain) -> Invoices` pair, which uses two separate inline empty sets — this codebase has both styles already and design.r1.md is explicit about the shared field for this pair.)

Expected: `Rules()` now yields 2 additional `TheoryData` entries; file compiles.

- [ ] **Step 3: Run the new rules to confirm they pass against the already-applied fix**

Run:
```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```
Expected: all `ModuleBoundaryRule` theory cases pass, including the two new ones (`"MarketingPerformance (Application) -> Invoices"` and `"MarketingPerformance (Domain) -> Invoices"`) — `Passed!`, 0 failed. This satisfies FR-3's second acceptance criterion.

- [ ] **Step 4: Attempt the FR-3 revert-verification described in spec.r1.md, and record the actual (documented-limitation) outcome**

spec.r1.md's third FR-3 acceptance criterion asks to simulate the original violation and confirm the new test fails, then restore. Do this exactly, using the previous task's commit as the "before the fix" baseline (`HEAD~1` at this point equals the commit right before `relocate-adapter-and-di-registration`'s commit, since that task's commit is the current `HEAD`):

```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
git checkout HEAD~1 -- backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs
git show HEAD~1:backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs > backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs
rm backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs
dotnet test --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```

Expected (verified by direct inspection of `EnumerateReferencedTypes`, lines 1004–1045 of this file, before writing this plan): **this specific revert does NOT make the two new rules fail** — all `ModuleBoundaryRule` cases, including the two new ones, still pass. This is not a mistake in the rules; it is a pre-existing, disclosed limitation of the `ModuleBoundariesTests` reflection technique, already called out by arch-review.r1.md's own Risks table ("no rule anywhere inspects a `Persistence.*` assembly... Accepted as an existing, codebase-wide limitation... out of scope to fix generally"):
- `EnumerateReferencedTypes`'s own doc comment states it "does not inspect method bodies" — it only walks attributes, fields, properties, constructor parameters, and method parameter/return types (reflection-level signatures), never IL/method-body contents.
- The original violation lived entirely in two places neither rule inspects: (a) the `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` call, which is a statement inside `AddMarketingPerformanceModule`'s method **body** (invisible to `EnumerateReferencedTypes`), and (b) the `_context.IssuedInvoices` LINQ query inside `IssuedInvoiceMonthlyRevenueSource.GetAsync`'s method **body**, in a class whose namespace (`Anela.Heblo.Persistence.Marketing`) was never one of the two rules' `InspectedNamespacePrefix` values (`Anela.Heblo.Application.Features.MarketingPerformance` / `Anela.Heblo.Domain.Features.MarketingPerformance`) in the first place.
- Confirmed independently by grepping `Anela.Heblo.Application/Features/MarketingPerformance` and `Anela.Heblo.Domain/Features/MarketingPerformance` for `Invoices`: the only matches are doc-comment text (not type references), both before and after the fix.

Do not treat a "still passes" result here as a bug to fix — extending `ModuleBoundariesTests` to inspect `Persistence.*` assemblies or method bodies is a structural change to the test harness itself, explicitly out of scope per spec.r1.md's Out of Scope section ("Auditing or fixing any other module-boundary violations... not a general sweep"). The two new rules still deliver real, spec-required value: they will catch any *future* direct reference from `MarketingPerformanceModule`/other MarketingPerformance Application- or Domain-layer types to `Invoices`' Domain/Application/Persistence namespaces via a field, property, constructor parameter, or method signature — which is the shape every other `-> Invoices` rule in this file already guards against.

Now restore the fix (discard this temporary revert):
```bash
git checkout HEAD -- backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs
rm -f backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs
git status
```
Expected: `git status` shows a clean working tree except for the uncommitted Step 1/Step 2 changes to `ModuleBoundariesTests.cs` (the allowlist field and the two new `Rules()` entries) — i.e., exactly what this task has changed so far, nothing from the temporary revert left behind.

- [ ] **Step 5: Full build and full test suite**

Run:
```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
dotnet build
dotnet test
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; full `dotnet test` run passes with 0 failed (FR-3's fourth acceptance criterion — the new rule runs as part of the standard suite, no opt-in flag).

- [ ] **Step 6: Commit**

```bash
cd /home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "$(cat <<'EOF'
Add MarketingPerformance -> Invoices module-boundary rules (fix #4290)

Adds two ModuleBoundaryRule entries (Application + Domain layer, mirroring
the existing Analytics (Application/Domain) -> Invoices pair) so a future
direct reference from MarketingPerformance into Invoices' Domain/Application/
Persistence namespaces is caught by dotnet test, closing the regression-guard
gap that let the just-fixed IssuedInvoiceMonthlyRevenueSource violation go
undetected.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01RC4VFf829MtzjZ7v5DP5ny
EOF
)"
```
Expected: commit succeeds; `git status` shows a clean working tree with one new commit on top of `relocate-adapter-and-di-registration`'s commit.

---

## Self-Review

- **FR-1** (move the adapter implementation into Invoices' persistence layer, namespace `Anela.Heblo.Persistence.Invoices`, behavior byte-for-byte unchanged, `dotnet build` clean) → `relocate-adapter-and-di-registration`, Steps 1–2, 6.
- **FR-2** (remove the registration + `using` from `MarketingPerformanceModule`, add them to `InvoicesModule`, confirm both modules are wired in composition with no ordering dependency, confirm `MarketingPerformanceRefreshService` still resolves `IMonthlyRevenueSource`) → `relocate-adapter-and-di-registration`, Steps 3–4, 6 (composition wiring in `ApplicationModule.cs` was confirmed unchanged/already-correct by direct inspection before writing this plan — no edit needed there).
- **FR-3 / the architecture-test FR** (new `MarketingPerformance -> Invoices` boundary rule(s), passing after the fix, running as part of standard `dotnet test`) → `add-marketingperformance-invoices-boundary-tests`, Steps 1–3, 5. Implemented as two rules (Application + Domain) per arch-review Decision 2 / Specification Amendment 1, since `ModuleBoundaryRule` cannot express a single three-prefix rule.
- **FR-3's third acceptance bullet** (revert should make the test fail) → attempted exactly as worded in Step 4 of `add-marketingperformance-invoices-boundary-tests`; the actual, verified-by-inspection outcome is that it does **not** fail, for reasons fully explained inline in that step (a pre-existing `ModuleBoundariesTests` limitation already disclosed in arch-review.r1.md's Risks table, not a defect introduced by this plan). This is flagged here rather than silently asserted as passing.
- **Arch-review Specification Amendment 2** (test-fallout fix: update `IssuedInvoiceMonthlyRevenueSourceTests.cs`'s `using`) → `relocate-adapter-and-di-registration`, Step 5.
- **Arch-review Specification Amendment 3** (`MarketingPerformanceRefreshServiceTests.cs` needs no change — its `Persistence.Marketing` usage is for `MarketingPerformanceRepository`, which isn't moving) → verified by direct inspection before writing this plan; no task step touches that file, which is correct.
- **NFR-1 / NFR-2** (no performance or security change) → satisfied by construction (pure move, verified byte-for-byte in Step 2 of the first task); no dedicated step needed since there is no new behavior to measure.
- **Out of Scope items** (no signature/DTO change, no query-logic change, no `ApplicationDbContext` split, no broader boundary sweep, no rename, no accessibility change) → respected throughout; the moved file's body and the class's `public` accessibility are untouched.
- No placeholders: every step above shows exact file paths, exact diffs/full file contents, and exact commands with stated expected output.
