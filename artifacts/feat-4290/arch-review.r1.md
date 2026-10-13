# Architecture Review: Fix MarketingPerformance → Invoices module-boundary violation (`IssuedInvoiceMonthlyRevenueSource`)

## Skip Design: true

No UI/UX surface exists or changes here. This is a namespace/file relocation, a DI-registration move, and a new architecture test — no MediatR contract, controller, or frontend change. `MarketingPerformanceRefreshService` continues to inject `IMonthlyRevenueSource` with an unchanged signature.

## Architectural Fit Assessment

This aligns exactly with an established, repeated pattern in this codebase: **provider-owns-adapter-and-registration** for cross-module read access, documented in `docs/architecture/development_guidelines.md` under "Cross-Module Communication Example: ILeafletKnowledgeSource" and already applied twice inside `InvoicesModule.cs` (`IInvoiceConsumptionSource` → `InvoiceConsumptionSourceAdapter` for PackingMaterials, `IInvoiceImportStatisticsSource` → `InvoiceImportStatisticsSourceAdapter` for Analytics). The consumer side is already correct — `IMonthlyRevenueSource` lives in `Anela.Heblo.Domain.Features.MarketingPerformance`, MarketingPerformance's own domain namespace — so this fix touches only the provider side. There is no new integration point to design; the interface contract, its call site (`MarketingPerformanceRefreshService`), and `MonthlyRevenueSnapshot`/`YearMonth` are all unchanged.

One structural nuance verified by reading the two sibling adapters directly (not assumed from the docs): `InvoiceConsumptionSourceAdapter` and `InvoiceImportStatisticsSourceAdapter` are `internal sealed` classes in `Anela.Heblo.Application.Features.Invoices.Infrastructure` that each wrap `IIssuedInvoiceRepository` — they do not touch `ApplicationDbContext` directly. `IssuedInvoiceMonthlyRevenueSource`, by contrast, is a `public` class that queries `ApplicationDbContext.IssuedInvoices` directly via LINQ/EF Core, and it already lives in the **Persistence** layer, not the Application layer. This is a materially different shape from the two "twin" adapters the spec cites — see Decision 1 below for why the spec's proposed destination (`Persistence/Invoices`, direct `DbContext` access, pure move) is still the right call for this fix, not a deviation to correct.

## Proposed Architecture

### Component Overview

```
Before:
┌─────────────────────────────────────────┐        ┌──────────────────────────────┐
│ MarketingPerformanceModule.cs (owns)     │        │ Anela.Heblo.Domain            │
│  registers:                              │        │  .Features.Invoices           │
│  IMonthlyRevenueSource                   │        │   IssuedInvoice (entity)      │
│   → IssuedInvoiceMonthlyRevenueSource ───┼───────►│         ▲                     │
│     (Persistence.Marketing)              │  direct│         │ DbSet<IssuedInvoice>│
└─────────────────────────────────────────┘  DbSet  └─────────┼─────────────────────┘
                                              access            ApplicationDbContext
                                                       (cross-module violation)

After:
┌───────────────────────────────┐          ┌───────────────────────────────────────┐
│ Anela.Heblo.Domain             │          │ InvoicesModule.cs (owns + registers)   │
│  .Features.MarketingPerformance│          │                                         │
│   IMonthlyRevenueSource ◄──────┼──────────┤  services.AddScoped<                   │
│   (contract, unchanged)        │implements│    IMonthlyRevenueSource,               │
└───────────────────────────────┘          │    IssuedInvoiceMonthlyRevenueSource>()  │
                                            │                                         │
                                            │  Anela.Heblo.Persistence.Invoices       │
                                            │   IssuedInvoiceMonthlyRevenueSource     │
                                            │    → _context.IssuedInvoices  (same-    │
                                            │       module DbSet access, now legal)   │
                                            └───────────────────────────────────────┘

MarketingPerformanceModule.cs no longer references Persistence.Marketing for this concern.
MarketingPerformanceRefreshService still injects IMonthlyRevenueSource, unaware of the move.
```

### Key Design Decisions

#### Decision 1: Keep `IssuedInvoiceMonthlyRevenueSource` as a direct-`DbContext` Persistence-layer class (do not force it into the `Application/Features/Invoices/Infrastructure` adapter shape)

**Options considered:**
- **A. Follow the spec literally** — move the file as-is to `Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs`, namespace `Anela.Heblo.Persistence.Invoices`, still querying `_context.IssuedInvoices` directly, `public`, no logic change.
- **B. Normalize it to match `InvoiceConsumptionSourceAdapter`/`InvoiceImportStatisticsSourceAdapter` exactly** — make it `internal sealed`, move it to `Anela.Heblo.Application.Features.Invoices.Infrastructure`, and have it delegate to `IIssuedInvoiceRepository` instead of `ApplicationDbContext` (extracting a new repository method first).

**Chosen approach:** Option A, exactly as the spec directs.

**Rationale:** `IssuedInvoiceMonthlyRevenueSource` was never shaped like the two sibling adapters — it is, and remains, a data-access class that talks straight to EF Core, which is precisely what `IssuedInvoiceRepository.cs` (already sitting in `Anela.Heblo.Persistence/Invoices/`) also does. Per ADR-004 in `development_guidelines.md`, repository-shaped implementations belong in `Anela.Heblo.Persistence`, with the DI binding owned by the feature module — that is what this fix produces. Option B would be a legitimate longer-term direction (route all Invoices read-access through `IIssuedInvoiceRepository` for consistency and testability-via-repository-mock), but it requires extending `IIssuedInvoiceRepository` with a new query method, changing `GetAsync`'s implementation shape, and picking a new access modifier — all of which the spec's Out of Scope section explicitly forbids ("Any change to the actual revenue-aggregation logic," "Renaming... only its location/namespace/registration move," no repository extraction implied or requested). Forcing Option B here would silently expand a boundary-fix task into a refactor and risk behavior drift in a revenue-reporting query with no test-plan coverage for the new code path. Keep the class `public` (per spec's Out of Scope item 7) — that matches its two Persistence.Invoices siblings (`IssuedInvoiceRepository`) more closely than it matches the `internal sealed` Application-layer adapters, since it now lives in that same folder and needs to be visible to `InvoicesModule.cs` in the Application project.

#### Decision 2: Split the FR-3 architecture test into two `ModuleBoundaryRule` entries, not one

**Options considered:**
- **A. One rule** with three "inspected" namespace prefixes (`Domain.Features.MarketingPerformance`, `Application.Features.MarketingPerformance`, `Persistence.Marketing`) as the spec's FR-3 text describes.
- **B. Two rules**, mirroring the existing `Analytics (Application) -> Invoices` / `Analytics (Domain) -> Invoices` pair — one per assembly, since `Persistence.Marketing` is never itself an "inspected" (consumer) namespace anywhere in this test file today.

**Chosen approach:** Option B. See **Specification Amendments** — this is a required correction, not a stylistic preference, because the test infrastructure's `ModuleBoundaryRule` record physically cannot express what FR-3 asks for.

**Rationale:** Read `ModuleBoundariesTests.cs` directly: `ModuleBoundaryRule` has exactly one `InspectedNamespacePrefix` (a single string, compared with `StartsWith`) and one `InspectedAssembly` (defaulting to `"Anela.Heblo.Application"`, loaded once via `Assembly.Load`). Every module pair that needs to guard both its Domain and Application layers today (Analytics → Catalog, Analytics → Invoices, Analytics → Bank) does so with **two separate `ModuleBoundaryRule` entries** — `"{Module} (Application) -> {Target}"` and `"{Module} (Domain) -> {Target}"` — not one rule with a multi-prefix inspection. `Persistence.*` is never used as an `InspectedAssembly` anywhere in the file (confirmed by search) — the pattern only ever inspects Application and Domain assemblies for outbound references, because Persistence-layer classes are expected to reference Domain entities of the module they belong to going forward, and cross-module leakage from Persistence is caught indirectly (by the fact that after the fix, `Persistence/Marketing/` contains no file referencing `Invoices` at all — verified: only `IssuedInvoiceMonthlyRevenueSource.cs` did, and it is being moved out).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Persistence/Invoices/
  IssuedInvoiceMonthlyRevenueSource.cs      # moved from Persistence/Marketing/, namespace
                                             # Anela.Heblo.Persistence.Invoices, unchanged body
                                             # + comment noting it implements MarketingPerformance's
                                             #   IMonthlyRevenueSource contract

backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
  + using Anela.Heblo.Domain.Features.MarketingPerformance;   (for IMonthlyRevenueSource)
  + registration (third cross-module adapter entry, alongside the existing two):
      services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();

backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs
  - using Anela.Heblo.Persistence.Marketing;   (remove — no longer referenced by this file)
  - services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();   (remove)

backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs
  - using Anela.Heblo.Persistence.Marketing;
  + using Anela.Heblo.Persistence.Invoices;
  (see Specification Amendments — this file is not mentioned in spec.r1.md but will not
  compile after FR-1 without this one-line import change; recommend also relocating it to
  backend/test/Anela.Heblo.Tests/Features/Invoices/ for consistency with the moved production
  file, though this is optional — a same-folder import fix is the minimum required change)

backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  + MarketingPerformanceInvoicesAllowlist (empty HashSet<string>, same style as
    DataQualityInvoicesAllowlist) declared near the other allowlists
  + two ModuleBoundaryRule entries appended to the Rules list, placed near the other
    "-> Invoices" rules (PackingMaterials -> Invoices, Analytics (Application/Domain) -> Invoices,
    DataQuality -> Invoices) for readability:
      "MarketingPerformance (Application) -> Invoices"
      "MarketingPerformance (Domain) -> Invoices"   (InspectedAssembly: "Anela.Heblo.Domain")
```

### Interfaces and Contracts

No interface changes. `IMonthlyRevenueSource` (in `Anela.Heblo.Domain.Features.MarketingPerformance`) keeps its exact signature:

```csharp
public interface IMonthlyRevenueSource
{
    Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken);
}
```

`IssuedInvoiceMonthlyRevenueSource` keeps implementing it unchanged, only its namespace (`Anela.Heblo.Persistence.Marketing` → `Anela.Heblo.Persistence.Invoices`) and file path change.

### Data Flow

Unchanged at runtime. `MarketingPerformanceRefreshService` (Application/Features/MarketingPerformance/Services) resolves `IMonthlyRevenueSource` from DI exactly as before; the only change is which module's `{Module}.cs` supplied the binding. `GetAsync` still queries `ApplicationDbContext.IssuedInvoices` with the same `AsNoTracking()`/grouping/currency logic, now from a file that is lexically inside the module (`Invoices`) that owns the `IssuedInvoice` entity — closing the compile-time cross-module reference instead of routing data differently.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-3 as literally worded (one rule, three inspected prefixes) doesn't match `ModuleBoundaryRule`'s single-prefix/single-assembly shape and won't compile or won't do what's intended | Medium | Implement as two rules per Decision 2 / Specification Amendments below, mirroring the `Analytics (Application/Domain) -> Invoices` precedent exactly. |
| Existing unit test `IssuedInvoiceMonthlyRevenueSourceTests.cs` has `using Anela.Heblo.Persistence.Marketing;` and will fail to compile once the production class moves | Low | Update that one `using` as part of FR-1's "move" (see Specification Amendments); this is a compile-time break, not a runtime risk, so it will be caught immediately by `dotnet build`. |
| DI double-registration or ordering hazard if the old registration isn't fully removed before the new one is added | Low | FR-2 is a cut-and-paste move of one line; `dotnet build` plus the DI-wiring/integration test named in FR-2's acceptance criteria will catch a leftover duplicate registration immediately (last-registration-wins semantics would otherwise mask it silently, as already called out for `IMonthlyAdCostSource`'s no-op/Flexi pattern in the same file — don't let that pattern's "last wins" comment nearby be mistaken as acceptable here). |
| Someone later adds a new file to `Persistence/Marketing/` that reaches into `Domain.Features.Invoices` directly (repeating today's violation) via something other than the two Application/Domain rules can see | Low | Accepted as an existing, codebase-wide limitation of the `ModuleBoundariesTests` pattern (no rule anywhere inspects a `Persistence.*` assembly) — out of scope to fix generally per spec's Out of Scope section; not unique to this change. |

## Specification Amendments

1. **FR-3 rule shape**: Replace the single three-prefix rule described in FR-3 with **two** `ModuleBoundaryRule` entries, exactly mirroring the existing `Analytics (Application) -> Invoices` / `Analytics (Domain) -> Invoices` pair:
   - `"MarketingPerformance (Application) -> Invoices"`, `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.MarketingPerformance"`, default `InspectedAssembly` ("Anela.Heblo.Application"), `ForbiddenNamespacePrefixes: ["Anela.Heblo.Domain.Features.Invoices", "Anela.Heblo.Application.Features.Invoices", "Anela.Heblo.Persistence.Invoices"]`, empty allowlist.
   - `"MarketingPerformance (Domain) -> Invoices"`, `InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.MarketingPerformance"`, `InspectedAssembly: "Anela.Heblo.Domain"`, same forbidden list, empty allowlist.
   - Do **not** attempt to add `Anela.Heblo.Persistence.Marketing` as a third inspected prefix — `ModuleBoundaryRule` doesn't support multiple inspected prefixes per rule, and no existing rule in this file ever treats a `Persistence.*` assembly as "inspected" (only as a forbidden target). After FR-1, `Persistence/Marketing/` contains zero references to `Invoices` (verified directly — only the file being moved referenced it), so no third rule is needed to close the original gap.

2. **Test-file fallout not mentioned in spec.r1.md**: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs` has `using Anela.Heblo.Persistence.Marketing;` and will not compile once FR-1 moves the production class out of that namespace. Add to FR-1's scope: update that `using` to `Anela.Heblo.Persistence.Invoices;`. (Optional, not required: relocate the test file itself to `backend/test/Anela.Heblo.Tests/Features/Invoices/` to mirror the production move — leaving it under `Features/MarketingPerformance/` is acceptable since it is testing a MarketingPerformance-facing contract's implementation and the spec doesn't ask for the relocation.)

3. **`MarketingPerformanceRefreshServiceTests.cs`** also has `using Anela.Heblo.Persistence.Marketing;`, but verified this is for `MarketingPerformanceRepository` (which is *not* moving) — no change needed there; noted here only so the implementer doesn't need to re-derive this.

## Prerequisites

None beyond a standard local dev environment. No migration, no config, no infrastructure change, no feature flag. The only "setup" step is doing FR-1 (file move) before FR-2 (DI move) before FR-3 (test add) in that order, since FR-2 and FR-3 both depend on the class existing at its new namespace to compile/pass.
