# Architecture Review: Relocate `IDepartmentClient`/`Department` to UserManagement Domain

## Skip Design: true

## Architectural Fit Assessment

This is a textbook module-boundary correction and aligns tightly with the project's Vertical Slice / Clean Architecture conventions (`docs/architecture/filesystem.md`, `docs/architecture/development_guidelines.md`): domain types belong under `Anela.Heblo.Domain/Features/{Feature}/` where `{Feature}` is the module that actually owns the concept, and "Feature cohesion" (all code for a feature findable in one place) is an explicit required practice.

I verified the finding directly in code:
- `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` and `IDepartmentClient.cs` are the only two department-related files under `Domain/Features/Analytics/`; nothing else in that folder touches them.
- `Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs` (the genuine Analytics-module consumer of department data) depends on `Rem.FlexiBeeSDK...IDepartmentClient` and maps into `Persistence.Analytics.Entities.Department` — a wholly separate type. It has zero reference to `Domain.Features.Analytics.IDepartmentClient`/`Department`.
- `FlexiDepartmentClient` implements `Domain.Features.Analytics.IDepartmentClient` and `FlexiDepartmentQueryService` consumes it, mapping into `Application.Features.UserManagement.Contracts.DepartmentDto` to satisfy `Application.Features.UserManagement.Services.IDepartmentQueryService`. This is purely a UserManagement data-access concern wearing an Analytics namespace.
- Grep across `backend/` confirms exactly the 4 non-domain files named in the spec reference the two types (`FlexiDepartmentClient.cs`, `FlexiDepartmentQueryService.cs`, `FlexiAdapterServiceCollectionExtensions.cs`, `FlexiDepartmentQueryServiceTests.cs`), plus the 2 files being moved. No hidden consumers exist.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already references `Anela.Heblo.Domain.Features.UserManagement` as a forbidden-namespace target in its "Authorization -> UserManagement" rule (line 380). That rule only forbids `Anela.Heblo.Application.Features.Authorization` from referencing it — it does not forbid the Flexi adapter (or anything else) from populating or depending on the namespace. Creating and populating `Domain/Features/UserManagement/` does not trip this or any other existing `ModuleBoundaryRule` in the file; I confirmed no rule in that suite inspects `Anela.Heblo.Domain.Features.Analytics` for forbidden references *to* `UserManagement`, and no rule inspects the Flexi adapter project at all.

There is no design/UI surface here — this is an internal type relocation with an unchanged public contract, hence `Skip Design: true`.

## Proposed Architecture

### Component Overview

```
Before:
  Domain.Features.Analytics.{Department, IDepartmentClient}
        ^
        | implements / uses (namespace only, no logical ownership)
        |
  Adapters.Flexi.Accounting.Departments.FlexiDepartmentClient
        ^
        | consumed by
        |
  Adapters.Flexi.Accounting.Departments.FlexiDepartmentQueryService
        (implements Application.Features.UserManagement.Services.IDepartmentQueryService)
        ^
        | consumed by
  Application.Features.UserManagement.UseCases.GetDepartments.GetDepartmentsHandler

After (namespace corrected, shape unchanged):
  Domain.Features.UserManagement.{Department, IDepartmentClient}   <-- moved here
        ^
        |
  Adapters.Flexi.Accounting.Departments.FlexiDepartmentClient      <-- using updated
        ^
        |
  Adapters.Flexi.Accounting.Departments.FlexiDepartmentQueryService <-- using updated
        ^
        |
  Application.Features.UserManagement.UseCases.GetDepartments.GetDepartmentsHandler (unchanged)

Untouched, parallel flow (different types entirely):
  Adapters.Flexi.Analytics.DepartmentSyncService
        -> Rem.FlexiBeeSDK...IDepartmentClient (SDK client, not domain)
        -> Persistence.Analytics.Entities.Department (EF entity, not domain model)
```

No new component is introduced; an existing pair of types is relabeled into the module that already consumes it end-to-end.

### Key Design Decisions

#### Decision 1: Target folder name — `UserManagement`, not `Users`
**Options considered:**
- Put `Department`/`IDepartmentClient` under the existing `Anela.Heblo.Domain/Features/Users/` folder (already present, holds `ICurrentUserService`/`CurrentUser`).
- Create a new `Anela.Heblo.Domain/Features/UserManagement/` folder.

**Chosen approach:** New `UserManagement` folder, matching `spec.r1.md` exactly.

**Rationale:** `Domain/Features/Users` is a distinct, narrowly-scoped concept ("who is the currently authenticated caller" — per ADR-005 and `development_guidelines.md`'s User Identity Resolution section). `Department` belongs to the broader UserManagement feature (department lookups for user/group administration), which already has its own `Application.Features.UserManagement` namespace with `Contracts/`, `Services/`, `UseCases/`. Domain types should mirror the Application-layer module name they serve — `Domain.Features.UserManagement` mirrors `Application.Features.UserManagement` exactly, consistent with every other module in the codebase (e.g. `Domain.Features.Catalog` ↔ `Application.Features.Catalog`). Conflating it with `Users` would merge two unrelated concepts and contradicts the spec's explicit "Out of Scope" note. This also happens to be the exact namespace the `ModuleBoundariesTests.cs` "Authorization -> UserManagement" rule already anticipates, so the new folder is filling a boundary the test suite was already written to expect rather than inventing an unplanned one.

#### Decision 2: No new `ModuleBoundaryRule` entries
**Options considered:**
- Add explicit rules constraining what `Domain.Features.UserManagement` may/may not reference, now that it has real content.
- Add nothing; rely on existing rules.

**Chosen approach:** Add nothing.

**Rationale:** The only existing rule mentioning this namespace ("Authorization -> UserManagement") governs `Application.Features.Authorization`'s outbound references, which are unaffected by this move — `Department`/`IDepartmentClient` are not exception/contract types Authorization would ever touch. No rule polices the `Anela.Heblo.Adapters.Flexi` project's dependencies (adapters are expected to depend on whichever domain feature they implement), so `FlexiDepartmentClient` referencing `Domain.Features.UserManagement` is unconstrained and correct. Introducing speculative new rules for a two-file, already-isolated domain slice would be scope creep beyond a pure relocation; if UserManagement's domain surface grows later and genuine cross-module coupling risk appears, that is the point to add a rule, not now.

## Implementation Guidance

### Directory / Module Structure

Create `backend/src/Anela.Heblo.Domain/Features/UserManagement/` (new folder — first content in it) containing exactly:
```
backend/src/Anela.Heblo.Domain/Features/UserManagement/
├── Department.cs          # moved from Features/Analytics/, namespace changed only
└── IDepartmentClient.cs   # moved from Features/Analytics/, namespace changed only
```
Delete both files from `backend/src/Anela.Heblo.Domain/Features/Analytics/` — do not leave forwarding stubs or type aliases; the spec calls for a clean move with zero remaining references, and `ModuleBoundariesTests.cs` best-practice in this codebase is to keep a fully-qualified, unambiguous mapping between namespace and folder location.

This is a file move + `namespace` line edit only. No new subfolders (`Contracts/`, `Services/`, etc.) are warranted at the Domain layer for two files of this size — matches the "Simple Features" pattern in `filesystem.md`, scaled down further since this is Domain, not Application.

### Interfaces and Contracts

No interface shape changes. For traceability, the exact before/after:

```csharp
// backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs
namespace Anela.Heblo.Domain.Features.UserManagement;

public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

```csharp
// backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs
namespace Anela.Heblo.Domain.Features.UserManagement;

public interface IDepartmentClient
{
    Task<IEnumerable<Department>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetDepartmentByIdAsync(string departmentId, CancellationToken cancellationToken = default);
}
```

Consumers to update — `using Anela.Heblo.Domain.Features.Analytics;` → `using Anela.Heblo.Domain.Features.UserManagement;` in each, plus the one fully-qualified base-interface reference:

1. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` — update the `using`, and change `public class FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient` to `Domain.Features.UserManagement.IDepartmentClient`. **Do not touch** the SDK alias line `using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;` — it disambiguates a different, unrelated `IDepartmentClient` and must remain exactly as-is.
2. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — update the `using` only.
3. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — update the `using Anela.Heblo.Domain.Features.Analytics;` at the top of the file. I confirmed by inspection that no other symbol in this file resolves through that `using` (all other Analytics-domain-adjacent registrations in the file go through `Anela.Heblo.Persistence.Analytics` and `Anela.Heblo.Adapters.Flexi.Analytics`, both separate `using` lines already present and unaffected). The `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` line itself needs no edit — it will resolve against the new `using`.
4. `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — update the `using` only; assertions and mock setup are unaffected since the type shape is identical.

Explicitly **do not modify**:
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs` — uses the FlexiBee SDK's own `IDepartmentClient` and `Persistence.Analytics.Entities.Department`, neither of which is affected.
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs` — same reason; spec FR-6 correctly calls this out to verify, not change.
- `Anela.Heblo.Application.Features.UserManagement.Contracts.DepartmentDto` and `Services.IDepartmentQueryService` — already correctly namespaced; out of scope.

### Data Flow

Unchanged end-to-end; only the namespace segment in the middle of the chain changes:

`GetDepartmentsHandler` (Application/UserManagement) → `IDepartmentQueryService` (Application/UserManagement/Services) → `FlexiDepartmentQueryService` (Adapters.Flexi) → `IDepartmentClient` **[now `Domain.Features.UserManagement`]** → `FlexiDepartmentClient` (Adapters.Flexi, in-memory cache, 10 min TTL, unchanged) → FlexiBee SDK's own `IDepartmentClient`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed reference to the old namespace left uncompiled or silently stale (e.g. a file not caught by initial grep) | Low | Run `grep -rn "Domain.Features.Analytics.IDepartmentClient\|Domain\.Features\.Analytics\.Department\b" backend/` after the change and confirm zero matches (excluding unrelated `AnalyticsProduct*` types); `dotnet build` will also fail loudly on any missed `using`/type reference since these are compile-time C# references, not reflection/config-based. |
| Accidentally editing the SDK alias in `FlexiDepartmentClient.cs` (`using IDepartmentClient = Rem.FlexiBeeSDK...`) while doing a broad find/replace on `IDepartmentClient` | Medium | Edit by exact string match on `Domain.Features.Analytics` only, never on the bare `IDepartmentClient` token; verify after the edit that the SDK alias line is byte-identical to before. |
| New `ModuleBoundariesTests.cs` violation from populating a previously-empty namespace that another rule silently depended on being empty | Low | Full solution `dotnet build` plus running the `ModuleBoundariesTests` suite specifically before declaring done (already required by NFR-3); confirmed by static review that no rule inspects `Domain.Features.Analytics` for outbound department references, and the one rule naming `Domain.Features.UserManagement` only constrains `Application.Features.Authorization`, which has no path to `Department`/`IDepartmentClient`. |
| Confusing this `Department` (domain model) with `Persistence.Analytics.Entities.Department` (EF entity) during the edit, e.g. an IDE auto-import picking the wrong one | Low | Both files under review explicitly qualify or `using`-scope the correct type; no file in the touched set references the EF entity, so a compile error would surface immediately if the wrong type were substituted. |

## Specification Amendments

None. The spec (`spec.r1.md`) is architecturally sound as written — the file list is verified accurate (I independently re-ran the grep and got the same five touched files plus two moved files), the module-boundary reasoning about `ModuleBoundariesTests.cs` is correct, and the FR/NFR breakdown maps cleanly onto the mechanical steps above. No additional files, folders, or DI changes are needed beyond what FR-1 through FR-7 already specify.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — this is a same-behavior, compile-time-only relocation within the existing `Anela.Heblo.Domain` project. Implementation can start immediately; sequence the file moves before the `using`-statement edits so the solution never sits in a state with both old and new files present under the same type names.
