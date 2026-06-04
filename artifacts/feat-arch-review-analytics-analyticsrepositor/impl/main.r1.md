All 7 commits are on the branch. The implementation is complete.

---

# Implementation: Decouple AnalyticsRepository from Invoices and Bank Modules

## What was implemented

Behavior-preserving refactor that removes direct EF Core cross-module access from `AnalyticsRepository`. Introduced two Consumer-Owned Contract interfaces in the Analytics Domain layer, implemented by `internal sealed` adapters owned and registered by the Invoices and Bank modules respectively. This mirrors the existing `IAnalyticsProductSource` / `CatalogAnalyticsSourceAdapter` pattern.

Architecture deviation from spec (documented in arch review): adapters inject `ApplicationDbContext` directly (not `IIssuedInvoiceRepository`/`IBankStatementImportRepository`) because the existing repository interfaces don't expose server-side aggregation. DI lifetime is **Scoped** (not Transient) because the adapters wrap a Scoped `ApplicationDbContext`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs` — Consumer-Owned Contract interface with XML doc referencing `IAnalyticsProductSource` as pattern precedent
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs` — Symmetric contract for bank statement statistics
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — `internal sealed` adapter with DateTimeKind normalization, server-side EF GroupBy, dictionary-based O(n) gap-fill
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — Symmetric adapter with ItemCount sum aggregation
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — Added `services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>()`
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — Added `services.AddScoped<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>()`
- `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` — Replaced 2-arg constructor with 3-arg; removed `ApplicationDbContext`, `IssuedInvoices` and `BankStatements` queries; pure delegation via `.ToList()`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — 5 adapter tests
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` — 5 adapter tests
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — 4 new zero-allowlist boundary rules (Analytics→Invoices, Analytics→Bank for both Application and Domain assemblies)
- `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs` — Updated constructor call from 2-arg to 3-arg

## Tests

- **InvoiceImportStatisticsSourceAdapterTests** (5): InvoiceDate branch, LastSyncTime branch (null filtering), empty range gap-fill, inclusive boundaries, gap-fill with missing days
- **BankStatementStatisticsSourceAdapterTests** (5): StatementDate branch (ItemCount sum), ImportDate branch, empty range, inclusive boundaries, gap-fill
- **ModuleBoundariesTests**: 22 total boundary rules pass; 4 new rules enforce Analytics cannot reference Invoices or Bank namespaces
- **Full suite**: 4387 passed, 3 skipped, 38 Docker-related failures (pre-existing Testcontainers — require Docker daemon, unrelated to this change)

## How to verify

```bash
# NFR-2 compliance
grep "_dbContext\.\(IssuedInvoices\|BankStatements\)" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs
# → 0 matches

# Module boundary enforcement
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
# → 22/22 PASS

# Adapter tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StatisticsSourceAdapterTests"
# → 10/10 PASS

# Full analytics feature suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Analytics"
# → 64/64 PASS
```

## Notes

- **DI lifetime deviation**: spec said Transient; arch review correctly overruled to Scoped because adapters wrap `ApplicationDbContext` (Scoped). Documented in commit messages.
- **Repository abstraction deviation**: spec preferred `IIssuedInvoiceRepository`/`IBankStatementImportRepository`; arch review overruled — neither interface exposes server-side aggregation. Adapters inject `ApplicationDbContext` directly, which is module-internal data access.
- **Gap-fill optimization**: implementation uses `ToDictionary()` + `TryGetValue()` for O(n) gap-fill instead of O(n²) `FirstOrDefault()`. Improves over the original `AnalyticsRepository` implementation.
- **Test location**: Invoice adapter tests placed under `Infrastructure/` subfolder (consistent with `InvoiceConsumptionSourceAdapterTests` precedent), not the plan's `Features/Invoices/` path. Correct improvement.
- **`AnalyticsRepository` delegation tests not added**: the two new delegation methods are trivial one-liners covered by adapter tests. Adding explicit `AnalyticsRepositoryTests` for them is a low-priority follow-up.

## PR Summary

Decouples `AnalyticsRepository` from direct cross-module EF Core access to `IssuedInvoices` and `BankStatements`, which violated the module boundary rules in `docs/architecture/development_guidelines.md`. This unblocks Phase 2 per-module DbContext splits and eliminates the schema-change blast radius from Invoices/Bank entity changes into Analytics persistence code.

Two Consumer-Owned Contract interfaces (`IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`) are declared in `Anela.Heblo.Domain.Features.Analytics`. The Invoices and Bank modules provide `internal sealed` adapters that fulfill the contracts using their own module-internal `ApplicationDbContext` access. `AnalyticsRepository` is reduced to pure delegation — three constructor-injected sources, no EF queries. Four new zero-allowlist `ModuleBoundaryRule` entries in `ModuleBoundariesTests` enforce the boundary in CI going forward.

DI lifetimes are Scoped (adapters wrap Scoped `ApplicationDbContext`). Gap-fill uses Dictionary lookups (O(n)) rather than the O(n²) `FirstOrDefault()` pattern from the original implementation.

### Changes
- `IInvoiceImportStatisticsSource.cs`, `IBankStatementStatisticsSource.cs` — new Consumer-Owned Contract interfaces in Analytics Domain
- `InvoiceImportStatisticsSourceAdapter.cs`, `BankStatementStatisticsSourceAdapter.cs` — `internal sealed` adapters with full aggregation logic and O(n) gap-fill
- `InvoicesModule.cs`, `BankModule.cs` — Scoped DI registrations (owned by provider modules)
- `AnalyticsRepository.cs` — constructor reduced from `(IAnalyticsProductSource, ApplicationDbContext)` to `(IAnalyticsProductSource, IInvoiceImportStatisticsSource, IBankStatementStatisticsSource)`; no EF queries remain
- `ModuleBoundariesTests.cs` — 4 new enforcement rules, zero allowlist entries
- `InvoiceImportStatisticsSourceAdapterTests.cs`, `BankStatementStatisticsSourceAdapterTests.cs` — 5 tests each (both date-type branches, empty range, inclusive boundaries, gap-fill)

## Status
DONE