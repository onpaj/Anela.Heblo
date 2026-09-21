# Architecture Review: Relocate Department and IDepartmentClient to the Analytics Domain Module

## Skip Design: true
Pure backend namespace/file relocation with no new or changed UI, no new endpoints, and no visual components. Design phase is not needed.

## Architectural Fit Assessment
The spec's analysis is correct and matches `docs/architecture/development_guidelines.md`, which states each module owns only the contract interfaces/DTOs it actually consumes and forbids modules leaking types into unrelated namespaces. `Department` and `IDepartmentClient` are pure domain contracts (no framework dependencies) that happen to sit under `Features/InvoiceClassification/` while being consumed exclusively by the Flexi accounting adapter (`FlexiDepartmentClient`, `FlexiDepartmentQueryService`) and wired up in `FlexiAdapterServiceCollectionExtensions.cs`. I independently verified (reading `Department.cs`, `IDepartmentClient.cs`, all four consumer files, and both test files) that:
- InvoiceClassification's own `ClassificationRule.Department`/`ClassificationHistory.Department` are plain `string?` fields — no coupling to the `Department` class exists there.
- `Anela.Heblo.Domain.Features.Analytics` already exists as a sibling domain folder and currently defines no `Department` type, so there is no naming collision risk.
- `DepartmentSyncService.cs` (and its test) use an entirely different `IDepartmentClient` (from `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments`) and a different `Department` (an EF entity in `Anela.Heblo.Persistence.Analytics.Entities`) — confirming the spec's correction that this file is out of scope.

This is a low-risk, mechanical move. The integration point is exclusively `using` directives (plus one fully-qualified reference and one alias) in the four consumer files identified in the spec.

## Proposed Architecture

### Component Overview
```
Before:
  Anela.Heblo.Domain.Features.InvoiceClassification
    ├── Department.cs                (unused by InvoiceClassification)
    └── IDepartmentClient.cs         (unused by InvoiceClassification)

  Anela.Heblo.Adapters.Flexi.Accounting.Departments
    ├── FlexiDepartmentClient.cs     --> implements ...InvoiceClassification.IDepartmentClient
    └── FlexiDepartmentQueryService.cs --> consumes ...InvoiceClassification.{IDepartmentClient, Department}

  FlexiAdapterServiceCollectionExtensions.cs --> DI registers ...InvoiceClassification.IDepartmentClient

After:
  Anela.Heblo.Domain.Features.Analytics
    ├── Department.cs                (moved, namespace updated)
    └── IDepartmentClient.cs         (moved, namespace updated)

  Anela.Heblo.Adapters.Flexi.Accounting.Departments
    ├── FlexiDepartmentClient.cs     --> implements ...Analytics.IDepartmentClient
    └── FlexiDepartmentQueryService.cs --> consumes ...Analytics.{IDepartmentClient, Department}

  FlexiAdapterServiceCollectionExtensions.cs --> DI registers ...Analytics.IDepartmentClient

  (untouched) Anela.Heblo.Adapters.Flexi.Analytics.DepartmentSyncService
    --> still uses Rem.FlexiBeeSDK's own IDepartmentClient + Persistence.Analytics.Entities.Department
        (different types, different namespaces — no relation to this move)
```

### Key Design Decisions

#### Decision 1: Target namespace — `Anela.Heblo.Domain.Features.Analytics` vs. new `OrgChart` module
**Options considered:**
- (a) Move into the existing `Anela.Heblo.Domain.Features.Analytics` folder/namespace.
- (b) Create a new `Anela.Heblo.Domain.Features.OrgChart` module, treating departments as a first-class domain concept.

**Chosen approach:** (a) — reuse the existing `Analytics` folder.

**Rationale:** Both real consumers (`FlexiDepartmentClient`, `FlexiDepartmentQueryService`) already live under `Adapters.Flexi.Accounting.Departments` and are wired for accounting/analytics sync purposes (the same `FlexiAdapterServiceCollectionExtensions.cs` registers them alongside other Analytics adapter services). Creating a brand-new `OrgChart` module for two small types is disproportionate scope for an arch-review cleanup and would require a new folder, potentially a new `Module.cs`, and a larger PR surface for zero functional benefit. `Analytics` already exists, has no naming collision, and is the module the issue itself names as the "natural home." Introducing `OrgChart` is left as a possible future decision if departments grow into a richer domain concept — not warranted today.

#### Decision 2: File move mechanics
**Options considered:**
- (a) Git `mv` (or delete + recreate) preserving file history, edit only the `namespace` line.
- (b) Recreate content by hand in the new location.

**Chosen approach:** (a) — move the file and edit only the namespace declaration line, leaving the rest of the class/interface body untouched.

**Rationale:** Preserves git blame/history for these two small types and minimizes diff noise, consistent with the "surgical changes" project convention (CLAUDE.md).

## Implementation Guidance

### Directory / Module Structure
- Move `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs`.
- Move `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` → `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`.
- No other files are created or deleted. No `Module.cs` changes are needed (this is a Domain-layer type move, not a new module registration).

### Interfaces and Contracts
- `Department` and `IDepartmentClient` signatures are unchanged — only their namespace changes from `Anela.Heblo.Domain.Features.InvoiceClassification` to `Anela.Heblo.Domain.Features.Analytics`.
- In `FlexiDepartmentClient.cs`, the class declaration `public class FlexiDepartmentClient : Domain.Features.InvoiceClassification.IDepartmentClient` must become `public class FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient` (the file already aliases the FlexiBee SDK's own `IDepartmentClient` via `using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;`, so keep using the fully-qualified form here rather than adding a second unqualified `using`, to avoid ambiguity with the existing alias).
- `FlexiDepartmentQueryService.cs`, `FlexiAdapterServiceCollectionExtensions.cs`, and `FlexiDepartmentQueryServiceTests.cs` each replace `using Anela.Heblo.Domain.Features.InvoiceClassification;` with `using Anela.Heblo.Domain.Features.Analytics;`.

### Data Flow
Unchanged. `FlexiDepartmentClient` still fetches from the FlexiBee SDK, caches, and returns `Department` instances; `FlexiDepartmentQueryService` still maps them to `DepartmentDto`; DI wiring still resolves the same concrete implementation for the same interface — only the interface's declaring namespace differs.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missing a `using` update leaves a dangling reference to the old namespace, breaking the build | Low | Spec enumerates the exact 4 files; run `dotnet build` and grep for `Domain.Features.InvoiceClassification.Department`/`IDepartmentClient` after the change to confirm zero remaining references. |
| Accidentally touching `DepartmentSyncService.cs` (a same-named-but-unrelated `IDepartmentClient`/`Department`) | Low | Spec explicitly calls out this file as out of scope with the reasoning; developer should verify via `git diff` that this file has zero changes. |
| Alias collision in `FlexiDepartmentClient.cs` between the FlexiBee SDK's aliased `IDepartmentClient` and the Domain's `IDepartmentClient` if both are pulled in unqualified | Low | Keep the base-interface reference fully qualified (`Domain.Features.Analytics.IDepartmentClient`) as it already is today, per Decision 2 above — do not add a second unqualified `using Anela.Heblo.Domain.Features.Analytics;` alongside the existing alias without checking for conflicts. |

## Specification Amendments
None — the spec's FR-1 through FR-4 fully and accurately scope the change; this review confirms them against the codebase.

## Prerequisites
None. No migrations, config, or infrastructure changes are required — this is a compile-time-only refactor.
