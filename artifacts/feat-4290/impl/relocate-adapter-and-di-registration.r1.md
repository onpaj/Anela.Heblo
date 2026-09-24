# Implementation: relocate-adapter-and-di-registration

## What was implemented

Moved `IssuedInvoiceMonthlyRevenueSource` from the `MarketingPerformance`-scoped
persistence folder (consumer) to the `Invoices`-scoped persistence folder
(provider, which owns the `IssuedInvoice` entity), and moved its DI
registration from `MarketingPerformanceModule.cs` to `InvoicesModule.cs`,
following the provider-owns-adapter pattern already used for
`IInvoiceConsumptionSource`/`IInvoiceImportStatisticsSource` in that same
file. This removes `MarketingPerformance`'s direct compile-time dependency on
the `Invoices` module's domain entity.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs` — moved via `git mv` from `.../Marketing/`; namespace changed to `Anela.Heblo.Persistence.Invoices`; added a cross-module adapter comment. Class body unchanged (byte-for-byte).
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs` — removed the `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` registration.
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — added `using Anela.Heblo.Domain.Features.MarketingPerformance;` and the `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` registration (with an explanatory comment mirroring the existing adapter entries).
- `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs` — updated `using Anela.Heblo.Persistence.Marketing;` → `using Anela.Heblo.Persistence.Invoices;`. Test body/class/namespace unchanged (relocating the test itself is optional per arch-review Specification Amendment 2).

## Deviation from the task-context spec

The task-context's Step 3 instructed removing the
`using Anela.Heblo.Persistence.Marketing;` line entirely from
`MarketingPerformanceModule.cs`. That namespace is *also* where
`MarketingPerformanceRepository` (the concrete class registered for
`IMarketingPerformanceRepository` at the line just above) lives — an
in-module reference, not a cross-module one, and unrelated to this task's
finding. Removing the whole `using` line broke the build with `CS0246: The
type or namespace name 'MarketingPerformanceRepository' could not be found`.
Fix: kept `using Anela.Heblo.Persistence.Marketing;` in place and only
removed the `IssuedInvoiceMonthlyRevenueSource` registration line, which is
the actual cross-module violation this task targets. No other part of the
spec was changed.

## Tests

- `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs` — `using` updated to the new namespace; no behavioral change to the tests themselves.
- No new tests added — this is a pure relocation of an existing adapter and its DI wiring; behavior is unchanged.

## How to verify

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~IssuedInvoiceMonthlyRevenueSourceTests|FullyQualifiedName~MarketingPerformanceRefreshServiceTests"
```

Both commands were run:
- `dotnet build`: `0 Error(s)`, `163 Warning(s)` — all pre-existing, none introduced by this change (verified none reference the four files touched here).
- `dotnet test` (filtered): `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12` in `Anela.Heblo.Tests.dll`, which includes `IssuedInvoiceMonthlyRevenueSourceTests` and `MarketingPerformanceRefreshServiceTests`.

## Notes

Pure move: no query, contract, or runtime behavior change. `IMonthlyRevenueSource`
still resolves for `MarketingPerformanceRefreshService` via the new
`InvoicesModule` registration (confirmed by the passing
`MarketingPerformanceRefreshServiceTests`).

## PR Summary

Moved `IssuedInvoiceMonthlyRevenueSource` (and its DI registration) from the
`MarketingPerformance` module to the `Invoices` module, which owns the
`IssuedInvoice` entity it queries — fixing the module-boundary violation
flagged in #4290. `MarketingPerformance` no longer has a compile-time
dependency on `Invoices`' domain entity; it depends only on the
`IMonthlyRevenueSource` contract, which `Invoices` now implements and
registers, mirroring the existing `IInvoiceConsumptionSource`/
`IInvoiceImportStatisticsSource` provider-owns-adapter pattern. Pure move —
no behavior, query, or contract change.

### Changes
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs` — moved from `Marketing/`, namespace updated, adapter comment added
- `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs` — removed the now-relocated registration
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — added the registration, owned by the provider module
- `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs` — updated `using` to the new namespace

## Status
DONE
