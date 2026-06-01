# Architecture Review: Move `AnalyticsRepository` to Persistence Layer

## Skip Design: true

## Architectural Fit Assessment

The refactor's *direction* is correct (restore inward dependency flow), but the spec contains two assumptions that don't hold in this codebase. Verified by reading the source:

1. **Spec claims `Anela.Heblo.Persistence` already references `Anela.Heblo.Application`.** It does not. `backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj:24-27` references only `Anela.Heblo.Domain` and `Anela.Heblo.Xcc`. Every existing repository implementation in Persistence (e.g. `ArticleRepository`, `BankStatementImportRepository`, `PurchaseOrderRepository`, `KnowledgeBaseRepository`) implements an interface that lives in **`Anela.Heblo.Domain.Features.*`**, not in Application. The project convention is **interfaces in Domain, implementations in Persistence**, and Persistence does not see Application at all.

2. **Spec FR-4 says remove the `ProjectReference` from `Anela.Heblo.Application.csproj`.** This cannot succeed at the Analytics-only scope. A grep for `using Anela.Heblo.Persistence` inside `Anela.Heblo.Application/` returns **26 hits across at least 12 modules** (Photobank, Smartsupp, Logistics, Manufacture, Marketing, DataQuality, Catalog, Journal, PackingMaterials, GiftSettings, MarketingInvoices, Invoices, CarrierCooling, GiftPackageManufacture). Removing the reference will break the build immediately. FR-4's grep-based acceptance criterion ("zero `using Anela.Heblo.Persistence` in Application") cannot be met without a much larger sweep that is explicitly Out of Scope.

The Analytics-specific portion of the refactor (move the implementation, keep the interface, rewire DI) is sound and low-risk. The `IAnalyticsRepository` interface, however, references DTO types that currently live in Application use-case folders (`DailyInvoiceCount`, `DailyBankStatementStatistics`, `ImportDateType`, `BankStatementDateType`) — so wherever the interface lives, those types must travel with it.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application                                              │
│                                                                      │
│  Features/Analytics/                                                 │
│    UseCases/GetMarginReport/GetMarginReportHandler  ──┐              │
│    UseCases/GetInvoiceImportStatistics/Handler      ──┤              │
│    UseCases/GetBankStatementImportStatistics/Handler──┤              │
│    DashboardTiles/InvoiceImportStatisticsTile       ──┤              │
│                                                       │ depends on   │
│                                                       ▼              │
│                                       IAnalyticsRepository           │
│                                       (abstraction — see Decision 1) │
└──────────────────────────────────────────────────────────────────────┘
                                                       ▲
                                                       │ implements
                                                       │
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Persistence                                              │
│                                                                      │
│  Features/Analytics/AnalyticsRepository.cs                           │
│    ├── ctor(IAnalyticsProductSource, ApplicationDbContext)           │
│    ├── Streaming/margin methods → delegate to IAnalyticsProductSource│
│    └── Import-statistics methods → query _dbContext directly         │
│                                                                      │
│  PersistenceModule.cs                                                │
│    └── services.AddScoped<IAnalyticsRepository, AnalyticsRepository> │
└──────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the `IAnalyticsRepository` interface lives

**Options considered:**
- **A. Keep in `Anela.Heblo.Application.Features.Analytics.Infrastructure` (spec's choice).** Requires adding a new `ProjectReference` from `Anela.Heblo.Persistence` → `Anela.Heblo.Application`, because the implementation in Persistence must see the interface and its DTOs. No other repository in the codebase does this; it introduces a new convention violation while fixing an older one.
- **B. Move the interface and its return-value DTOs to `Anela.Heblo.Domain.Features.Analytics` (project convention).** Matches every existing repository (`IArticleRepository`, `IBankStatementImportRepository`, `IPurchaseOrderRepository`, …). Persistence's existing Domain reference is sufficient; no new `ProjectReference` is needed. Requires moving four small DTOs (`DailyInvoiceCount`, `DailyBankStatementStatistics`, `ImportDateType`, `BankStatementDateType`) from `UseCases/` folders into Domain.
- **C. Keep interface in Application AND DTOs in Application, add Persistence → Application reference.** Same as A.

**Chosen approach: B.** Match the established convention.

**Rationale:** The whole point of the refactor is to align Analytics with the rest of the codebase. Option A creates a one-off where Persistence references Application — the inverse of the desired Clean Architecture flow and inconsistent with every other repository here. The DTOs being moved aren't request/response contracts (those are `GetInvoiceImportStatisticsRequest/Response`, which stay in their use-case folders); they are domain-level aggregate records and belong in Domain. The move is mechanical (≤4 files, no logic change) and unblocks Persistence implementing the interface cleanly.

#### Decision 2: Folder name in Persistence

**Options considered:** `Persistence/Analytics/` (spec suggestion) vs. `Persistence/Features/Analytics/` (matches the existing `Persistence/Features/Article/`, `Persistence/Features/Bank/`, `Persistence/Features/Leaflet/` convention).

**Chosen approach: `Persistence/Features/Analytics/AnalyticsRepository.cs`.**

**Rationale:** Persistence already has a `Features/` umbrella folder for repositories grouped by domain feature. New code should follow it. Older repos under `Persistence/Catalog/`, `Persistence/Dashboard/`, etc., predate this convention; new work should use `Features/`.

#### Decision 3: Scope of FR-4 (removing the `ProjectReference`)

**Options considered:**
- **A. Delete FR-4.** Acknowledge that 26 `using Anela.Heblo.Persistence` references in 12 other modules block the reference removal; the Analytics-only fix cannot complete the full architectural goal.
- **B. Retain FR-4 and expand scope to fix all 12 modules.** Out-of-scope per the brief and the spec's "Out of Scope" section.
- **C. Replace FR-4 with a narrower invariant.** "No NEW dependency from Application on Persistence is introduced; the Analytics-specific `using Anela.Heblo.Persistence` and `ApplicationDbContext` references are eliminated. The `ProjectReference` removal is deferred to a follow-up sweep."

**Chosen approach: C.** Make this a Specification Amendment (below).

**Rationale:** The Analytics refactor on its own genuinely reduces Persistence coupling in Application; that's a meaningful, reviewable win. Promising full reference removal at this scope sets the implementation up to fail at the build step. The deferred broader sweep is already implied by the brief's "broader sweep is a separate effort" caveat.

## Implementation Guidance

### Directory / Module Structure

Final-state file layout:

```
backend/src/Anela.Heblo.Domain/Features/Analytics/
├── AnalyticsProduct.cs                       (unchanged)
├── AnalyticsProductType.cs                   (unchanged)
├── ProductGroupingMode.cs                    (unchanged)
├── IAnalyticsRepository.cs                   (MOVED from Application)
├── DailyInvoiceCount.cs                      (MOVED from UseCases/GetInvoiceImportStatistics/)
├── DailyBankStatementStatistics.cs           (MOVED from UseCases/GetBankStatementImportStatistics/)
├── ImportDateType.cs                         (MOVED — extract if currently nested)
└── BankStatementDateType.cs                  (MOVED — extract if currently nested)

backend/src/Anela.Heblo.Application/Features/Analytics/
├── AnalyticsModule.cs                        (drop the IAnalyticsRepository registration)
├── Infrastructure/                           (KEEP folder; may now contain only Application-side helpers, or be removed if it ends up empty — confirm before deleting)
└── UseCases/                                 (handlers update `using` imports to Domain)

backend/src/Anela.Heblo.Persistence/Features/Analytics/
└── AnalyticsRepository.cs                    (NEW — class body verbatim from the old file, namespace updated)

backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
   + services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
```

Implementation namespace: `namespace Anela.Heblo.Persistence.Features.Analytics;` (matches `Anela.Heblo.Persistence.Features.Article`, etc.).

### Interfaces and Contracts

- `IAnalyticsRepository` — method signatures unchanged; namespace becomes `Anela.Heblo.Domain.Features.Analytics`.
- `AnalyticsRepository` constructor signature unchanged: `(IAnalyticsProductSource productSource, ApplicationDbContext dbContext)`.
- DI binding: a single `services.AddScoped<IAnalyticsRepository, AnalyticsRepository>()` in `PersistenceModule.cs`. **Remove the identical line from `AnalyticsModule.cs:25`.**
- `IAnalyticsProductSource` location: verify before moving. If it lives in Application, the Persistence-side implementation needs to see it. Two acceptable paths: (i) move `IAnalyticsProductSource` to Domain alongside the rest, or (ii) leave it in Application and confirm it is resolved via DI at runtime — but the compile-time reference still requires its namespace to be visible to Persistence. Path (i) is preferred for consistency.

### Data Flow

For both characteristic use cases, the flow becomes:

```
HTTP request
  → MediatR handler in Application (e.g. GetInvoiceImportStatisticsHandler)
  → IAnalyticsRepository  [Domain abstraction]
  → AnalyticsRepository   [Persistence implementation]
     ├── streaming/margin path → IAnalyticsProductSource (delegation, no DbContext)
     └── import-statistics path → ApplicationDbContext LINQ query, returns Domain DTOs
  → handler shapes Response DTO (Application)
  → controller returns to client
```

No behavior change. EF Core query trees in `GetInvoiceImportStatisticsAsync` (lines 115–185 of the current file) and `GetBankStatementImportStatisticsAsync` (lines 213–283) move verbatim.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer follows spec FR-4 literally and tries to remove `ProjectReference` from Application → solution-wide build break. | High | Spec Amendment 1 (below) downgrades FR-4 explicitly. Implementation plan must call this out. |
| `IAnalyticsProductSource` interface remains in Application; moving the repository implementation to Persistence creates a missing reference at compile time. | Medium | Audit `IAnalyticsProductSource` location before starting. If in Application, move it to Domain in the same change set. |
| Adding the new DI binding in `PersistenceModule` while leaving the old binding in `AnalyticsModule` registers the service twice (last-write-wins). Removing both leaves it unregistered, breaking startup. | Medium | Plan must include an atomic swap: add to Persistence in the same commit that removes from `AnalyticsModule.cs:25`. Verify with an integration test that resolves `IAnalyticsRepository` from a built `IServiceProvider`. |
| Test file `AnalyticsRepositoryTests.cs` and the four handler test files reference the moved types via `using Anela.Heblo.Application.Features.Analytics.Infrastructure;` (interface) and `using Anela.Heblo.Application.Features.Analytics.UseCases.*;` (DTOs). They will fail to compile. | Low | Mechanical `using`-only updates in 5 test files. No assertion changes — the test project already references both Application and Domain via transitive graph. |
| Folder `Features\Analytics\Infrastructure\` may end up empty; the `.csproj` has an explicit `<Folder Include="Features\Analytics\Infrastructure\" />` entry (line 50) that will dangle. | Low | Delete the empty folder and the corresponding `<Folder>` entry from `Anela.Heblo.Application.csproj` if the directory becomes empty. |
| Spec NFR-3 ("Application contains no `using Anela.Heblo.Persistence`") cannot be verified true at the Analytics scope because 25 other usages remain. | Medium | Amendment 1: narrow NFR-3 to "no `using Anela.Heblo.Persistence` or reference to `ApplicationDbContext` remains *within `Features/Analytics/`*". |

## Specification Amendments

**Amendment 1 — narrow FR-4 and NFR-3 to Analytics scope.**
The `<ProjectReference Include="../Anela.Heblo.Persistence/...">` in `Anela.Heblo.Application.csproj` (line 41) **must NOT be removed** in this change. 26 `using Anela.Heblo.Persistence` references across ≥12 other Application modules (Photobank, Smartsupp, Logistics, Manufacture, Marketing, DataQuality, Catalog, Journal, PackingMaterials, GiftSettings, MarketingInvoices, Invoices, CarrierCooling, GiftPackageManufacture) keep the reference compulsory. Restate the acceptance criteria as:

- Zero `using Anela.Heblo.Persistence` statements and zero `ApplicationDbContext` references **inside `backend/src/Anela.Heblo.Application/Features/Analytics/`**.
- A follow-up backlog item is filed for the broader sweep + `ProjectReference` removal.

**Amendment 2 — move `IAnalyticsRepository` interface (and its return-DTO types) to Domain, not Application.**
Override FR-2's "keep interface in Application" instruction. Justification: project-wide convention places every repository interface in `Anela.Heblo.Domain.Features.*` (verified for Article, Bank, Purchase, Packaging, KnowledgeBase, MeetingTasks, Leaflet, GridLayouts, DataQuality, FeatureFlags, Dashboard, Catalog/Stock, InvoiceClassification, BackgroundJobs — every entry in `PersistenceModule.cs`). The `Anela.Heblo.Persistence` project does **not** reference `Anela.Heblo.Application`, and shouldn't start to. Specifically:
- Move `IAnalyticsRepository.cs` → `Anela.Heblo.Domain/Features/Analytics/`.
- Move `DailyInvoiceCount.cs`, `DailyBankStatementStatistics.cs`, `ImportDateType`, `BankStatementDateType` → same Domain folder.
- Update all consumers' `using` statements to point at the new Domain namespace.

**Amendment 3 — implementation location inside Persistence.**
Per project convention, place the moved implementation at `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`, not `backend/src/Anela.Heblo.Persistence/Analytics/AnalyticsRepository.cs` as the spec proposes. Namespace: `Anela.Heblo.Persistence.Features.Analytics`.

**Amendment 4 — verify and (if needed) move `IAnalyticsProductSource`.**
Before moving the implementation, confirm where `IAnalyticsProductSource` lives. If it is in `Anela.Heblo.Application`, move it to `Anela.Heblo.Domain.Features.Analytics` in the same change set, otherwise the Persistence-side `AnalyticsRepository` will not compile.

## Prerequisites

- No database migration, schema change, configuration change, or new package reference is required.
- No new project references are required (under Amendments 2 & 3). Persistence already references Domain; Domain already exists.
- Implementation plan must batch the changes as a single atomic commit set:
  1. Move types into Domain (interface + DTOs + `IAnalyticsProductSource` if applicable).
  2. Create `Persistence/Features/Analytics/AnalyticsRepository.cs` with verbatim method bodies.
  3. Delete the old `Application/Features/Analytics/Infrastructure/AnalyticsRepository.cs`.
  4. Add `IAnalyticsRepository` registration in `PersistenceModule.cs`.
  5. Remove the duplicate registration from `AnalyticsModule.cs:25`.
  6. Update `using` statements in 5 handler/tile files and 5 test files.
  7. `dotnet build` + `dotnet format` + run Analytics test suite.