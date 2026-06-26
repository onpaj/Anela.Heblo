Now I have enough context to write the architecture review.

```markdown
# Architecture Review: Decouple AnalyticsRepository from Invoices and Bank Modules

## Skip Design: true

Pure backend refactor with no UI/UX work. All HTTP contracts, MediatR responses, and the OpenAPI-generated TypeScript client remain unchanged.

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions. The codebase already enforces the **Consumer-Owned Contract** inversion described in `docs/architecture/development_guidelines.md` (section "Cross-Module Communication Example: ILeafletKnowledgeSource") and applies it across several module seams: `IAnalyticsProductSource` → `CatalogAnalyticsSourceAdapter`, `ILeafletKnowledgeSource` → `KnowledgeBaseLeafletSourceAdapter`, `IInvoiceConsumptionSource` → `InvoiceConsumptionSourceAdapter`, etc. The Analytics → Catalog inversion is also already covered by reflection-based boundary tests in `ModuleBoundariesTests.cs:337-358`. The new Invoices/Bank seams are the same shape and slot in next to those tests with no new pattern invention.

Integration points:
- **Domain layer**: two new interface files under `Anela.Heblo.Domain/Features/Analytics/`. No new DTOs — `DailyInvoiceCount`, `DailyBankStatementStatistics`, `ImportDateType`, `BankStatementDateType` already live there.
- **Application layer**: two new adapter classes inside `Application/Features/Invoices/Infrastructure/` and `Application/Features/Bank/Infrastructure/`, mirroring `CatalogAnalyticsSourceAdapter`'s location and visibility (`internal sealed`).
- **DI registration**: in `InvoicesModule.AddInvoicesModule` and `BankModule.AddBankModule`. `AnalyticsRepository` registration in `PersistenceModule.cs:134` stays untouched (DI resolves the new constructor automatically).
- **Test layer**: `ModuleBoundariesTests.Rules()` must be extended with two new rules (Analytics → Invoices, Analytics → Bank) so the boundary is enforced in CI going forward — without this, the refactor can silently regress.

The biggest reality-check finding: neither `IIssuedInvoiceRepository` nor `IBankStatementImportRepository` currently exposes anything suitable for server-side `GROUP BY` aggregation. Both materialize entities into `IEnumerable<T>` collections. Adding `IQueryable<T>` to those interfaces would leak EF; adding Analytics-shaped aggregation methods to `IBankStatementImportRepository` (which lives in `Anela.Heblo.Domain.Features.Bank`) would create a reverse Domain-on-Analytics coupling. The right call is **adapter injects `ApplicationDbContext` directly** — the adapter is owned by the Invoices/Bank module, so direct DbContext use is module-internal data access, not a boundary violation. The spec already permits this fallback; I am elevating it to the recommended default.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ Domain.Features.Analytics  (Consumer — owns contracts)                 │
│   IAnalyticsRepository                                                 │
│   IAnalyticsProductSource              ← existing                      │
│   IInvoiceImportStatisticsSource       ← NEW (FR-1)                    │
│   IBankStatementStatisticsSource       ← NEW (FR-2)                    │
│   DailyInvoiceCount, DailyBankStatementStatistics, *DateType enums     │
└────────────────────────────────────────────────────────────────────────┘
              ▲                          ▲                       ▲
              │ implements               │ implements            │ implements
              │                          │                       │
┌─────────────┴────────────┐  ┌──────────┴──────────────┐  ┌─────┴────────────┐
│ Persistence.Analytics    │  │ Application.Invoices    │  │ Application.Bank │
│   AnalyticsRepository    │  │   .Infrastructure       │  │   .Infrastructure│
│   (Scoped, owned by      │  │  InvoiceImportStats…    │  │  BankStatement…  │
│    Persistence layer)    │  │  Adapter                │  │  StatsAdapter    │
│                          │  │  (internal sealed)      │  │  (internal sealed)│
│   ctor:                  │  │                         │  │                  │
│    IAnalyticsProductSource│ │  ctor:                  │  │  ctor:           │
│    IInvoiceImportStats…  │  │   ApplicationDbContext  │  │  ApplicationDbCtx│
│    IBankStatementStats…  │  │                         │  │                  │
└──────────────────────────┘  └─────────────────────────┘  └──────────────────┘
              │                          │                       │
              │ delegates                │ EF query              │ EF query
              │                          ▼                       ▼
              │            ┌──────────────────────────────────────────────┐
              └──────────► │ ApplicationDbContext (shared, Phase 1)       │
                           │   IssuedInvoices set ← Invoices module entity│
                           │   BankStatements set ← Bank module entity    │
                           └──────────────────────────────────────────────┘
```

Resolution chain at runtime:
1. MediatR handler resolves `IAnalyticsRepository` (Scoped).
2. DI provides `AnalyticsRepository` with three injected dependencies, including the two new sources.
3. `GetInvoiceImportStatisticsAsync` → `IInvoiceImportStatisticsSource` (Scoped) → adapter calls EF directly → returns `IReadOnlyList<DailyInvoiceCount>` → `AnalyticsRepository` calls `.ToList()` → handler.

### Key Design Decisions

#### Decision 1: Adapter injects `ApplicationDbContext` directly (not `IIssuedInvoiceRepository` / `IBankStatementImportRepository`)

**Options considered:**
1. Add `IQueryable<T> Query()` to the existing repository interfaces — leaks EF abstraction across the seam and breaks the testability of the repository pattern.
2. Add typed aggregation methods (e.g. `GetDailyCountsAsync(...)`) to `IIssuedInvoiceRepository` / `IBankStatementImportRepository` — requires the repository contract to return Analytics-owned DTOs, which couples Bank Domain (`Anela.Heblo.Domain.Features.Bank`) to Analytics types. For Invoices it is bearable (interface lives in Application), but inconsistent across the two adapters.
3. **Adapter takes `ApplicationDbContext` directly** — adapter lives inside the Invoices/Bank module, so DbContext access is module-internal. No interface surface changes, no new abstraction churn, behavior is byte-for-byte identical to today's code (the same EF query expressions just move file).

**Chosen approach:** Option 3.

**Rationale:** Both `IssuedInvoiceRepository` and `BankStatementImportRepository` already inject `ApplicationDbContext`; the adapter doing the same is consistent with each module's existing data-access style. It preserves server-side aggregation (NFR-1) trivially because the EF query stays as-is. It does not pollute the repository interfaces with Analytics-shaped concerns or `IQueryable<>` leaks. When Phase 2 (per-module DbContexts) lands, only the adapter constructor changes — same as every other Invoices/Bank data-access type.

**Caveat:** If a future story introduces a sane repository method (e.g. a generic projecting aggregator), the adapter should migrate. Worth noting in the PR description so the reviewer doesn't churn over it.

#### Decision 2: Adapter DI lifetime — **Scoped**, not Transient

**Options considered:** Transient (matches `IAnalyticsProductSource` registration in `CatalogModule.cs:51`), Scoped (matches the lifetime of the injected `ApplicationDbContext`).

**Chosen approach:** Register both new adapters as **Scoped**.

**Rationale:** The Catalog adapter wraps `ICatalogRepository`, which is a stateless service over the catalog domain — Transient works fine there. The new adapters wrap `ApplicationDbContext`, which is **Scoped** by EF convention. A Transient wrapper around a Scoped dependency is legal (no captive dependency), but creating a new wrapper instance every time `AnalyticsRepository` (itself Scoped) is resolved adds zero value and obscures the natural per-request lifecycle. The spec's "match the precedent" instinct is good intent but the precedent it is matching has a different shape underneath. I am overruling the spec on this point — recommend Scoped, document the deviation in the PR.

#### Decision 3: Module boundary enforcement test must be added in the same PR

**Options considered:** Add to `ModuleBoundariesTests.Rules()` now; add as follow-up.

**Chosen approach:** Add the two new rules in the same PR.

**Rationale:** The whole point of the refactor is to make the boundary enforceable. Without the test, the next developer can re-introduce `_dbContext.IssuedInvoices` inside an Analytics handler and CI will not catch it. The pattern is already in place (Analytics → Catalog rule lines 337-358 of `ModuleBoundariesTests.cs`) — copying it costs ~30 lines. This is a hard prerequisite for the refactor to deliver its stated value.

#### Decision 4: Widen `IAnalyticsRepository` returns from `List<>` to `IReadOnlyList<>` — **no**, keep `List<>`

**Options considered:** Widen the interface signature; keep `List<>` and `.ToList()` at the boundary.

**Chosen approach:** Keep `List<>` on `IAnalyticsRepository`; adapter returns `IReadOnlyList<>`; `AnalyticsRepository` calls `.ToList()`.

**Rationale:** Pure refactor. NFR-3 says no external contract changes. Widening the interface is a callable-surface change that risks unexpected propagation (handlers may assign to `List<>` locals, tests may assert on `List<>` shape). The `.ToList()` cost is negligible (one allocation, same data) and keeps the refactor strictly behavior-preserving. Defer the widening to a follow-up if anyone actually wants it.

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Domain/Features/Analytics/
  IInvoiceImportStatisticsSource.cs        ← FR-1
  IBankStatementStatisticsSource.cs        ← FR-2

backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/
  InvoiceImportStatisticsSourceAdapter.cs  ← FR-3

backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/
  BankStatementStatisticsSourceAdapter.cs  ← FR-4
```

Modified files:
```
backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs
  - Drop _dbContext.IssuedInvoices / _dbContext.BankStatements queries.
  - Drop both gap-fill loops (move into adapters).
  - New ctor parameters; remove the Invoices and Bank using-directives.
  - Drop ApplicationDbContext field if no Analytics-owned operation still needs it
    (per spec: StreamProductsWithSalesAsync / GetProductAnalysisDataAsync delegate
    to _productSource and never touch _dbContext, so the field is unused after
    this change and MUST be removed).

backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
  + services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();

backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs
  + services.AddScoped<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>();

backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  + Two new ModuleBoundaryRule entries (see Prerequisites section).
```

### Interfaces and Contracts

`IInvoiceImportStatisticsSource` and `IBankStatementStatisticsSource` are exactly as the spec defines them (FR-1, FR-2). Refining one point only:

- Both interface XML doc comments **must reference `IAnalyticsProductSource` and the development-guidelines section** ("Cross-Module Communication Example") so the next developer sees the pattern context inline. Concrete proposed wording for both:

  ```csharp
  /// <summary>
  /// Consumer-Owned Contract owned by the Analytics module. Implemented by the
  /// Invoices module via <c>InvoiceImportStatisticsSourceAdapter</c>; DI registration
  /// lives in <c>InvoicesModule</c>. Mirrors the inversion pattern in
  /// <c>docs/architecture/development_guidelines.md</c> ("Cross-Module Communication
  /// Example") and the precedent in <see cref="IAnalyticsProductSource"/>.
  /// </summary>
  ```

`AnalyticsRepository` constructor shape:

```csharp
public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly IInvoiceImportStatisticsSource _invoiceImportStatisticsSource;
    private readonly IBankStatementStatisticsSource _bankStatementStatisticsSource;

    public AnalyticsRepository(
        IAnalyticsProductSource productSource,
        IInvoiceImportStatisticsSource invoiceImportStatisticsSource,
        IBankStatementStatisticsSource bankStatementStatisticsSource)
    {
        _productSource = productSource;
        _invoiceImportStatisticsSource = invoiceImportStatisticsSource;
        _bankStatementStatisticsSource = bankStatementStatisticsSource;
    }
    // ...
}
```

`ApplicationDbContext` is removed from the constructor entirely (see Directory section).

### Data Flow

**Use case A: `GetInvoiceImportStatisticsHandler` requests daily counts**

```
Handler
  → IAnalyticsRepository.GetInvoiceImportStatisticsAsync(start, end, dateType)
    → AnalyticsRepository delegates to _invoiceImportStatisticsSource
      → InvoiceImportStatisticsSourceAdapter.GetDailyCountsAsync(...)
        → Normalize start/end to DateTimeKind.Unspecified
        → EF query on ApplicationDbContext.IssuedInvoices (server-side GROUP BY)
        → Project to DailyInvoiceCount with DateTimeKind.Utc
        → Gap-fill missing dates with zero rows (in-memory)
        → Return IReadOnlyList<DailyInvoiceCount>
      → AnalyticsRepository calls .ToList()
    → Returns List<DailyInvoiceCount> (unchanged contract)
  → Handler maps to response DTO
```

**Use case B: `GetBankStatementImportStatisticsHandler`** — symmetric; sums `ItemCount` per day.

**Use case C: `StreamProductsWithSalesAsync` / `GetProductAnalysisDataAsync`** — already delegated to `_productSource` today, unchanged. Confirmed by reading `AnalyticsRepository.cs:26-42`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF query shape silently regresses (e.g. moving GroupBy after a `.ToList()`) and aggregation becomes client-side | High | One characterization test per adapter that asserts on `await query.ToListAsync()` row counts AND a separate test that enables EF query-log capture (`UseLoggerFactory` with a `TestLogger`) and asserts the captured SQL contains `GROUP BY` and `COUNT(`. Spec FR-6 already calls for SQL inspection — make it a real assertion, not a manual review note. |
| Gap-fill ordering changes (e.g. `.OrderBy` removed or reapplied differently) | Medium | Add explicit assertion in adapter tests that the returned `Date` sequence is strictly ascending and contiguous over `[startDate.Date, endDate.Date]`. |
| `ApplicationDbContext` removal from `AnalyticsRepository` accidentally breaks an unrelated method (e.g. a margin/analysis method that quietly reads from `_dbContext`) | Medium | Before removing the field, grep `_dbContext` references inside `AnalyticsRepository.cs`. From the read at lines 1–236, only the two refactored methods use it — safe to remove. Confirm with a clean `dotnet build` after removal. |
| Module-boundary regression introduced by a future PR (someone adds `using Anela.Heblo.Domain.Features.Invoices;` back to Analytics) | High | **Mandatory:** add `ModuleBoundaryRule` entries for Analytics → Invoices and Analytics → Bank in the same PR (see Prerequisites). Without this, the refactor's central guarantee is unenforced. |
| Lifetime mismatch — adapter Transient over Scoped DbContext leads to two adapter instances per request, each holding its own resolved DbContext reference | Low | Register both adapters as **Scoped** (Decision 2). Matches the DbContext lifetime, prevents accidental double-resolution. |
| `InvoiceImportStatisticsTile.cs` (dashboard tile that consumes `IAnalyticsRepository`) reports `source = "AnalyticsRepository"` in telemetry; behavior preservation extends to that label | Low | Label is a static string in the tile; the refactor leaves it intact. Flag in PR description that the source label stays the same so observability stays consistent. |
| Test project `Anela.Heblo.Tests` doesn't currently have a `Features/Bank/` or `Features/Invoices/` adapter test folder; mirroring conventions may be ambiguous | Low | Use `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs` and the symmetric Bank path. Co-locate with existing per-feature test directories. |

## Specification Amendments

1. **DI lifetime — Scoped, not Transient (FR-3 / FR-4 / API section "DI registration").** The spec mandates Transient to mirror `IAnalyticsProductSource`. The precedent is misaligned: the new adapters wrap `ApplicationDbContext` (Scoped), so Scoped is the semantically correct lifetime. See Decision 2 for rationale. Update FR-3 and FR-4 acceptance criteria to register `services.AddScoped<...>` instead of `AddTransient<...>`.

2. **Drop the "prefer `IIssuedInvoiceRepository` / `IBankStatementImportRepository`" guidance (FR-3 / FR-4).** Neither interface exposes a usable aggregation surface, and extending either introduces churn that competes with this refactor's purpose. Adapters inject `ApplicationDbContext` directly. The spec already permits this fallback — promote it from "if necessary" to the chosen approach. See Decision 1.

3. **Remove `ApplicationDbContext` from `AnalyticsRepository` constructor (FR-5).** Spec says "kept only if still needed". Per read of `AnalyticsRepository.cs`, no Analytics-owned method outside the two refactored ones touches `_dbContext`. Make removal of the field and the `using Anela.Heblo.Persistence;` directive a **hard** acceptance criterion, not a conditional.

4. **Mandatory `ModuleBoundariesTests` additions.** Spec FR-6/FR-7 cover behavior preservation and adapter tests but do not require regression-prevention boundary tests. Add a new acceptance criterion: two `ModuleBoundaryRule` entries (Analytics → Invoices, Analytics → Bank) appended to `ModuleBoundariesTests.Rules()`, each forbidding `Anela.Heblo.Domain.Features.{Invoices,Bank}`, `Anela.Heblo.Application.Features.{Invoices,Bank}`, and `Anela.Heblo.Persistence.{Invoices,Bank}` from Analytics namespaces (both `Anela.Heblo.Application.Features.Analytics` and `Anela.Heblo.Domain.Features.Analytics`). The Analytics → Catalog rule at lines 337-358 is the copy-paste template. See Prerequisites.

5. **SQL-shape verification (NFR-1).** Spec calls for manual EF log inspection. Promote to an automated assertion: one test per adapter that captures EF SQL via `LoggerFactory.Create(builder => builder.AddProvider(captureProvider))` and asserts the captured query contains `GROUP BY` and the expected aggregate. Manual-review verification rots; automated verification holds.

6. **Adapter test location.** Spec says "live in the same test project / folder as the existing Invoices and Bank module tests (mirror existing conventions)". Concrete paths: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs`. Test class visibility must use `InternalsVisibleTo` if absent, since adapters are `internal sealed` — confirm `Anela.Heblo.Application.csproj` already exposes internals to the test project before writing the tests.

## Prerequisites

1. **Confirm `InternalsVisibleTo` on `Anela.Heblo.Application`.** Adapters are `internal sealed`. If the test project cannot already see internals of `Anela.Heblo.Application`, the adapter tests will not compile. Check by searching `Anela.Heblo.Application.csproj` and any `AssemblyInfo.cs` for `InternalsVisibleTo("Anela.Heblo.Tests")`. The `CatalogAnalyticsSourceAdapter` precedent (also `internal sealed`) is already tested, so this is almost certainly in place — verify before starting.

2. **Boundary test additions (must ship in the same PR).** Add to `ModuleBoundariesTests.Rules()`:

   ```csharp
   new ModuleBoundaryRule(
       Name: "Analytics (Application) -> Invoices",
       InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Invoices",
           "Anela.Heblo.Application.Features.Invoices",
           "Anela.Heblo.Persistence.Invoices",
       },
       Allowlist: new HashSet<string>(StringComparer.Ordinal)),

   new ModuleBoundaryRule(
       Name: "Analytics (Application) -> Bank",
       InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Bank",
           "Anela.Heblo.Application.Features.Bank",
           "Anela.Heblo.Persistence.Bank",
       },
       Allowlist: new HashSet<string>(StringComparer.Ordinal)),

   // Optional but recommended: Domain-side counterparts mirroring the
   // Analytics (Domain) -> Catalog rule already at lines 348-358.
   new ModuleBoundaryRule(
       Name: "Analytics (Domain) -> Invoices",
       InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Invoices",
           "Anela.Heblo.Application.Features.Invoices",
           "Anela.Heblo.Persistence.Invoices",
       },
       Allowlist: new HashSet<string>(StringComparer.Ordinal),
       InspectedAssembly: "Anela.Heblo.Domain"),

   new ModuleBoundaryRule(
       Name: "Analytics (Domain) -> Bank",
       InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
       ForbiddenNamespacePrefixes: new[]
       {
           "Anela.Heblo.Domain.Features.Bank",
           "Anela.Heblo.Application.Features.Bank",
           "Anela.Heblo.Persistence.Bank",
       },
       Allowlist: new HashSet<string>(StringComparer.Ordinal),
       InspectedAssembly: "Anela.Heblo.Domain"),
   ```

   Note: `AnalyticsRepository` lives in `Anela.Heblo.Persistence.Features.Analytics`, which is **not** covered by any of the four rules above. That's intentional — the goal is to keep the Application and Domain Analytics modules clean. The Persistence-side `AnalyticsRepository` will no longer reference Invoices/Bank entities after this refactor, but enforcing that via a 5th rule on `Anela.Heblo.Persistence.Features.Analytics` is optional. **Recommended:** add it for symmetry, since the whole motivation is unblocking Phase 2 per-module DbContexts.

3. **No infrastructure / migration changes.** Spec is correct — zero schema changes, zero new packages, zero configuration changes, no Key Vault secret updates.

4. **Verify `ApplicationDbContext` reads after field removal.** Before deleting the `_dbContext` field from `AnalyticsRepository`, `grep -n "_dbContext" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` should return zero matches at the end of the refactor. Confirmed clean per current source read — only the two refactored methods reference it.

5. **Verify `IAnalyticsProductSource` registration timing.** `PersistenceModule` registers `AnalyticsRepository` (Scoped); `InvoicesModule` and `BankModule` register the new adapters. All three are wired in `Program.cs` composition. DI does not require ordering — resolution happens at request time — so no ordering changes are needed. Skim `Program.cs` once to confirm all three modules are wired.
```