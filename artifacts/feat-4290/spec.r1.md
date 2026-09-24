# Specification: Fix MarketingPerformance → Invoices module-boundary violation (`IssuedInvoiceMonthlyRevenueSource`)

## Summary
`IssuedInvoiceMonthlyRevenueSource` — the adapter that implements MarketingPerformance's `IMonthlyRevenueSource` contract by querying `ApplicationDbContext.IssuedInvoices` — is currently owned, placed, and DI-registered by the consumer module (`MarketingPerformance`) instead of the provider module (`Invoices`), which owns the `IssuedInvoice` entity. This is a documented "Direct access to another module's entities" violation of `docs/architecture/development_guidelines.md`. The fix relocates the adapter's implementation file and its DI registration into the `Invoices` module, following the exact `ILeafletKnowledgeSource` → `KnowledgeBaseLeafletSourceAdapter` inversion pattern already used elsewhere in this codebase (including twice already inside `InvoicesModule.cs` itself, for `IInvoiceConsumptionSource` and `IInvoiceImportStatisticsSource`).

## Background
This is a GitHub architecture-review finding (#4290), not a user feature request. `IssuedInvoiceMonthlyRevenueSource.cs` currently lives at `backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs` (namespace `Anela.Heblo.Persistence.Marketing`), and is registered in `MarketingPerformanceModule.AddMarketingPerformanceModule` (`backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs:22`):

```csharp
services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
```

Its `GetAsync` implementation queries `_context.IssuedInvoices` directly (lines 27–34 of the current file), where `IssuedInvoice` is a domain entity owned by `Anela.Heblo.Domain.Features.Invoices` — a different vertical slice. `development_guidelines.md` explicitly forbids "Direct access to another module's entities" and documents the required inversion pattern for exactly this situation (the *Cross-Module Communication Example: ILeafletKnowledgeSource* section):

1. **Consumer (A) defines the contract** in its own `Contracts/`/domain folder.
2. **Provider (B) implements the contract via an adapter**, living in provider B's `Infrastructure/`.
3. **Provider (B) registers the DI binding** in its own `{Module}.cs`; the consumer never touches this registration.

Confirmed by direct inspection of the repo at `/home/user/worktrees/feature-4290-Arch-Review-Marketing-Issuedinvoicemonthlyrevenues`:

- The consumer side is already correct: `IMonthlyRevenueSource` lives at `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/IMonthlyRevenueSource.cs`, inside MarketingPerformance's own domain namespace. **This file does not need to move.**
- The provider side is wrong: the adapter implementation and its DI registration both sit in MarketingPerformance-owned locations instead of Invoices-owned ones.
- `InvoicesModule.cs` (`backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`) already contains two adapters following the correct pattern for other consumer modules — `IInvoiceConsumptionSource` → `InvoiceConsumptionSourceAdapter` (for PackingMaterials) and `IInvoiceImportStatisticsSource` → `InvoiceImportStatisticsSourceAdapter` (for Analytics) — each with an explanatory comment ("Cross-module contract: Invoices implements X's IY via an adapter. DI registration owned by provider (Invoices), not consumer..."). The fix for `IMonthlyRevenueSource` should read as a third, consistent entry in that same list.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already has reflection-based boundary rules for `PackingMaterials -> Invoices`, `Analytics (Application) -> Invoices`, `Analytics (Domain) -> Invoices`, and `DataQuality -> Invoices`, each asserting the consumer module's namespaces contain no references to Invoices' `Domain`/`Application`/`Persistence` namespaces. **There is currently no `MarketingPerformance -> Invoices` (or `MarketingPerformance -> *` at all) rule in this file** — which is precisely why the existing violation was never caught by CI, and why leaving this gap unfilled would let the same class of bug regress silently even after the fix.

## Functional Requirements

### FR-1: Move the adapter implementation into the Invoices module's persistence layer
Relocate the file currently at `backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs` to `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs`, matching the existing `Anela.Heblo.Persistence.Invoices` folder that already holds `IssuedInvoiceRepository` and other Invoices-owned persistence classes (as referenced by `InvoicesModule.cs`'s `using Anela.Heblo.Persistence.Invoices;`).

Change the file's namespace declaration from `Anela.Heblo.Persistence.Marketing` to `Anela.Heblo.Persistence.Invoices`.

The class body (constructor, `GetAsync`, private constants, XML doc comment) is otherwise unchanged — this is a pure move/rename, not a rewrite. It should also gain a short adapter comment consistent with the ones in `InvoicesModule.cs`, e.g. noting it implements MarketingPerformance's `IMonthlyRevenueSource` contract, so a future reader immediately sees why an Invoices-namespaced class implements an interface from another module's domain namespace.

**Acceptance criteria:**
- `IssuedInvoiceMonthlyRevenueSource.cs` no longer exists under `backend/src/Anela.Heblo.Persistence/Marketing/`.
- The file exists at `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs` with namespace `Anela.Heblo.Persistence.Invoices`.
- The class still implements `Anela.Heblo.Domain.Features.MarketingPerformance.IMonthlyRevenueSource` and still queries `_context.IssuedInvoices` — behavior (query logic, grouping, currency/wholesale rules) is byte-for-byte unchanged except for namespace and the added explanatory comment.
- `dotnet build` succeeds with no new warnings introduced by the move.

### FR-2: Move the DI registration from `MarketingPerformanceModule` to `InvoicesModule`
Remove `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` from `MarketingPerformanceModule.AddMarketingPerformanceModule` (`backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs:22`), and remove the now-unused `using Anela.Heblo.Persistence.Marketing;` import from the top of that file.

Add the equivalent registration to `InvoicesModule.AddInvoicesModule` (`backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`), alongside the two existing cross-module adapter registrations (`IInvoiceConsumptionSource`/`IInvoiceImportStatisticsSource`), using the same comment style:

```csharp
// Cross-module contract: Invoices implements MarketingPerformance's IMonthlyRevenueSource
// via an adapter. DI registration owned by provider (Invoices), not consumer
// (MarketingPerformance) — mirrors the IInvoiceConsumptionSource/IInvoiceImportStatisticsSource
// pattern above.
services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
```

This requires adding `using Anela.Heblo.Domain.Features.MarketingPerformance;` (for `IMonthlyRevenueSource`) and `using Anela.Heblo.Persistence.Invoices;` (already present) to `InvoicesModule.cs`.

**Acceptance criteria:**
- `MarketingPerformanceModule.cs` no longer references `IssuedInvoiceMonthlyRevenueSource` or the `Anela.Heblo.Persistence.Marketing` namespace anywhere.
- `InvoicesModule.cs` registers `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();` inside `AddInvoicesModule`.
- Both `AddMarketingPerformanceModule` and `AddInvoicesModule` are invoked during application composition (confirm via existing `ApplicationModule.cs` / `Program.cs` wiring) so the binding is registered exactly once, with no ordering dependency between the two module-registration calls (DI container resolves `IMonthlyRevenueSource` the same way regardless of module registration order, since only `InvoicesModule` now registers it).
- `MarketingPerformanceRefreshService` (the sole consumer, injecting `IMonthlyRevenueSource`) continues to resolve correctly at runtime — verified by existing/updated integration or DI-wiring tests.

### FR-3: Add a `MarketingPerformance -> Invoices` module-boundary test
Add a new boundary rule to `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, following the exact shape of the existing `PackingMaterials -> Invoices` / `Analytics -> Invoices` / `DataQuality -> Invoices` rules (see lines ~453, ~542, ~553, ~644 of that file): assert that no type under `Anela.Heblo.Domain.Features.MarketingPerformance`, `Anela.Heblo.Application.Features.MarketingPerformance`, or `Anela.Heblo.Persistence.Marketing` references `Anela.Heblo.Domain.Features.Invoices`, `Anela.Heblo.Application.Features.Invoices`, or `Anela.Heblo.Persistence.Invoices`, with an empty allowlist (mirroring `DataQualityInvoicesAllowlist`, since after FR-1/FR-2 there should be zero legitimate references — the `IMonthlyRevenueSource` contract itself lives in MarketingPerformance's own domain namespace, not Invoices').

This is the regression guard called for by the finding: the underlying reason the original violation was never caught is that no boundary test existed for this module pair at all.

**Acceptance criteria:**
- A new test rule/case named consistently with existing entries (e.g. `"MarketingPerformance -> Invoices"`) exists in `ModuleBoundariesTests.cs`.
- The test passes after FR-1/FR-2 are applied (i.e., MarketingPerformance's namespaces contain zero references to Invoices' Domain/Application/Persistence namespaces).
- Reverting FR-1/FR-2 (i.e., simulating the original violation) causes this specific test to fail — confirm this locally before finalizing, then leave the fix applied. (Do not leave the revert in place; this is a one-time verification step, not a permanent change.)
- The test runs as part of the standard `dotnet test` / architecture test suite with no new opt-in flag required.

## Non-Functional Requirements

### NFR-1: Performance
No behavioral or query-plan change. `GetAsync`'s LINQ/SQL against `IssuedInvoices` is copied verbatim; this is a namespace/file/DI-registration move only. No new database round-trips, no change to `AsNoTracking()` usage, no change to indexes or query shape. Expected wall-clock impact: none.

### NFR-2: Security
No change to authentication, authorization, or data exposure. `MonthlyRevenueSnapshot` fields returned by `GetAsync` are unchanged. No new secrets, connection strings, or external calls are introduced.

## Data Model
No schema or entity changes. `IssuedInvoice` (owned by `Anela.Heblo.Domain.Features.Invoices`) and `MonthlyRevenueSnapshot` / `YearMonth` (owned by `Anela.Heblo.Domain.Features.MarketingPerformance`) are unchanged. This is purely a code-organization/DI-wiring fix; no migration is required.

## API / Interface Design
No public HTTP API, MediatR request/response, or frontend contract changes. The only "interface" affected is the internal C# contract `IMonthlyRevenueSource` (unchanged signature) and where its implementation is registered in the DI container. `MarketingPerformanceRefreshService` continues to inject `IMonthlyRevenueSource` exactly as before; it is unaware of and unaffected by which module now provides the binding.

## Dependencies
- Depends on the existing `Anela.Heblo.Persistence.Invoices` namespace/folder already present in the repo (used for `IssuedInvoiceRepository` et al.) as the destination for the moved file.
- Depends on `InvoicesModule.AddInvoicesModule` and `MarketingPerformanceModule.AddMarketingPerformanceModule` both being called during composition (already the case; see `ApplicationModule.cs`) — no new module registration call needs to be added or removed.
- No new external services, NuGet packages, or npm packages are introduced.
- No frontend/OpenAPI client regeneration is needed (no public API surface changes).

## Out of Scope
- Any change to `IMonthlyRevenueSource`'s method signature, `MonthlyRevenueSnapshot`, or `YearMonth`.
- Any change to the actual revenue-aggregation logic, currency handling, or wholesale/retail classification rules inside `GetAsync`.
- Splitting `ApplicationDbContext` into per-module contexts (tracked separately as ADR-001/Phase 2 future work — this fix operates entirely within "Phase 1: single shared `ApplicationDbContext`," consistent with how `InvoicesModule`'s other two adapters already work).
- Auditing or fixing any other module-boundary violations beyond this specific consumer/provider pair (e.g., not a general sweep of all Marketing-scoped persistence files) — this spec is scoped strictly to `IssuedInvoiceMonthlyRevenueSource` per issue #4290.
- Renaming `IssuedInvoiceMonthlyRevenueSource` itself. The class name is fine as-is (it already names the entity and its role); only its location/namespace/registration move.
- Adding an `internal` access modifier to the adapter class. The existing sibling adapters in `Invoices/Infrastructure` (`InvoiceConsumptionSourceAdapter`, `InvoiceImportStatisticsSourceAdapter`) are not shown as `internal` in `InvoicesModule.cs`'s usings, and `KnowledgeBaseLeafletSourceAdapter` is `internal sealed`; this spec does not mandate a specific accessibility change — leave the class `public` as it is today, consistent with the two existing Invoices-owned adapters, which is the closer precedent since the class is moving into that same module.

## Open Questions

None.

## Status: COMPLETE
