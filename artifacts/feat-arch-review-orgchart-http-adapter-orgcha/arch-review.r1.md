Now I have enough verified context to write the architecture review.

```markdown
# Architecture Review: Relocate OrgChartService HTTP adapter to Infrastructure

## Skip Design: true

Backend-only structural refactor. No UI components, screens, or visual changes — only folder/namespace relocation of one C# class.

## Architectural Fit Assessment

The proposal aligns precisely with the project's documented Clean Architecture / Vertical Slice convention. `docs/architecture/filesystem.md` (lines 32–33) explicitly defines:

- `Features/{Feature}/Services/` — *"Domain services and business logic"*
- `Features/{Feature}/Infrastructure/` — *"Feature infrastructure"*

Verified facts from the codebase:

1. **OrgChartService.cs is unambiguously infrastructure.** It does nothing but `HttpClient.GetAsync` → `JsonSerializer.Deserialize` → wrap exceptions. Zero domain rules or policy.
2. **OrgChart is the only feature module without an `Infrastructure/` folder.** All other modules I sampled (`Bank`, `Catalog`, `ExpeditionList`, `FileStorage`, `Invoices`, `Leaflet`, `MeetingTasks`) have one.
3. **Reference surface is tiny.** Grep confirms only 5 files reference `OrgChartService` / `OrgChart.Services`: the three OrgChart files themselves, `GetOrganizationStructureHandler.cs` (depends on the interface — unaffected by moving the concrete class), and `ApplicationModule.cs` (uses the `AddOrgChartServices` extension — unaffected). No backend tests reference `OrgChartService`.
4. **The codebase already shows mild convention drift.** `MeetingTasks/Services/` contains `GraphPlannerService` and `ClaudeMeetingTaskExtractor` (clearly external adapters), and `ExpeditionList/Services/` contains `FileSystemPrintQueueSink`. Fixing OrgChart is correct, but the broader inconsistency should be acknowledged (see Risks).

Integration points the move touches: nothing runtime. Only namespace resolution at compile time in `OrgChartModule.cs`.

The spec is correct, minimal, and faithful to the documented rule. The architectural decision worth being explicit about is **where the interface lives**.

## Proposed Architecture

### Component Overview

```
Features/OrgChart/
├── Contracts/                                 (unchanged)
│   └── OrgChartResponse, *Dto.cs
├── Services/                                  (domain boundary)
│   └── IOrgChartService.cs                    ← interface STAYS here
├── Infrastructure/                            (new folder)
│   └── OrgChartService.cs                     ← concrete adapter MOVED here
├── UseCases/
│   └── GetOrganizationStructure/
│       └── GetOrganizationStructureHandler.cs (depends on Services.IOrgChartService)
├── OrgChartOptions.cs                         (unchanged)
└── OrgChartModule.cs                          (gains one using directive)

Dependency direction (compile-time):
   UseCases ──depends on──▶ Services (interface)
   Infrastructure ──implements──▶ Services (interface)
   Module ──wires──▶ Services + Infrastructure
```

### Key Design Decisions

#### Decision 1: Interface placement — `Services/` vs `Infrastructure/`
**Options considered:**
- (A) Move both `IOrgChartService` and `OrgChartService` into `Infrastructure/`.
- (B) Keep `IOrgChartService` in `Services/`; move only the concrete class to `Infrastructure/` (the spec's choice).
- (C) Promote `IOrgChartService` to `Contracts/` since it is a feature-facing contract.

**Chosen approach:** (B). The interface remains in `Services/`; only the concrete `OrgChartService` moves.

**Rationale:** `IOrgChartService` is the **abstraction the use-case handler depends on**. It belongs on the domain side of the boundary so the application layer doesn't depend on infrastructure namespaces. This matches the well-known Dependency Inversion convention (the consumer owns the interface). Option (A) would force handlers to `using ...Infrastructure;`, defeating the purpose of the split. Option (C) is plausible but `Contracts/` in this codebase holds DTOs, not service abstractions — keeping it as-is preserves the project's existing semantic.

#### Decision 2: Preserve git history via `git mv`
**Options considered:** plain delete-and-create vs `git mv`.
**Chosen approach:** `git mv` (or an equivalent rename detected by git).
**Rationale:** `git log --follow` and `git blame` continuity matter for forensic work on adapter quirks. The spec already mandates this in NFR-4.

#### Decision 3: Do not refactor while moving
**Chosen approach:** Byte-identical contents except the `namespace` line.
**Rationale:** This refactor's value is the boundary correction. Mixing logic changes (retry, Polly, logging cleanup, JSON options consolidation) would obscure the diff, defeat history detection, and expand the test surface. Note these as follow-ups (see Specification Amendments).

## Implementation Guidance

### Directory / Module Structure

Create exactly one new directory and move one file:

```
+ backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/
+ backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs   (from Services/)
- backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs
~ backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs                   (one using added)
```

Execution sequence:
1. `git mv backend/src/Anela.Heblo.Application/Features/OrgChart/Services/OrgChartService.cs backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs`
2. Edit the `namespace` line to `Anela.Heblo.Application.Features.OrgChart.Infrastructure;`
3. Add `using Anela.Heblo.Application.Features.OrgChart.Infrastructure;` to `OrgChartModule.cs` (keep existing `using ...Services;` line).
4. `dotnet build` then `dotnet format` then `dotnet test`.

### Interfaces and Contracts

No interface changes. The contract `IOrgChartService` continues to live at:

```csharp
// Features/OrgChart/Services/IOrgChartService.cs
namespace Anela.Heblo.Application.Features.OrgChart.Services;

public interface IOrgChartService
{
    Task<OrgChartResponse> GetOrganizationStructureAsync(CancellationToken cancellationToken = default);
}
```

`OrgChartModule.cs` after the change must contain exactly these two using directives for the wiring line:

```csharp
using Anela.Heblo.Application.Features.OrgChart.Services;        // for IOrgChartService
using Anela.Heblo.Application.Features.OrgChart.Infrastructure;  // for OrgChartService
// ...
services.AddHttpClient<IOrgChartService, OrgChartService>();
```

### Data Flow

Unchanged at runtime. Compile-time only:

```
GetOrganizationStructureHandler
       │ depends on
       ▼
IOrgChartService                       (Services namespace)
       ▲
       │ implemented by
OrgChartService                        (Infrastructure namespace — NEW)
       │ uses
       ▼
HttpClient + OrgChartOptions ──► external DataSourceUrl ──► OrgChartResponse JSON
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Build breaks because `OrgChartModule.cs` can't resolve `OrgChartService` after the namespace change | LOW | Spec FR-4 mandates adding the new `using` line; `dotnet build` catches it immediately. |
| Git rename detection fails (file appears as delete + add), losing `--follow` history | LOW | Use `git mv`; verify with `git log --follow --oneline Infrastructure/OrgChartService.cs` before commit. Default rename threshold (50%) is safe because contents are byte-identical apart from the namespace line. |
| Hidden reflection / DI scanning relies on the old namespace | VERY LOW | DI is explicit (`AddHttpClient<IOrgChartService, OrgChartService>()`); no reflection-based scanners in the OrgChart module. Verified by reading `OrgChartModule.cs`. |
| Convention drift elsewhere (e.g. `MeetingTasks/Services/GraphPlannerService.cs`, `ExpeditionList/Services/FileSystemPrintQueueSink.cs`) makes future readers think OrgChart is now the outlier in the opposite direction | MEDIUM | Out of scope here per the spec. Track as a follow-up to migrate other adapters and then add a NetArchTest rule to prevent regression (see Specification Amendments). |
| Stale references in docs/comments still say `OrgChart.Services.OrgChartService` | LOW | Grep `docs/` and code comments for the fully qualified name after the move; update any hits. None found in current grep, but re-verify post-move. |

## Specification Amendments

The spec is already tight and correct. Three small additions worth incorporating:

1. **Verification step in FR-6:** Add `git log --follow backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` as an explicit acceptance check that history continuity survived the move. (Currently NFR-4 mandates the outcome but does not specify a verification command.)

2. **Sanity grep after the move:** Add to acceptance criteria a repository-wide grep for `OrgChart.Services.OrgChartService` (fully-qualified) and for any stale comments/docs referencing the old location, ensuring zero hits other than `IOrgChartService` (which legitimately remains in `Services`).

3. **Follow-up tracker (not in this PR):** The spec correctly lists "introducing an architecture-test (e.g. NetArchTest)" and "auditing other potentially-misplaced files" as out of scope. Recommend filing those as separate tickets immediately after merge so the boundary becomes enforceable, not just documented. Candidates spotted while exploring: `MeetingTasks/Services/GraphPlannerService.cs`, `MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`, `MeetingTasks/Services/IPlaudClient.cs` and related Plaud files, `ExpeditionList/Services/FileSystemPrintQueueSink.cs`, `Invoices/Services/InvoiceImportService.cs` (review whether it is domain logic or import plumbing).

## Prerequisites

None. The change requires:

- No migrations.
- No configuration changes (`OrgChartOptions.SectionName` and the `DataSourceUrl` key are unchanged).
- No infrastructure changes.
- No new NuGet packages.
- No coordination with frontend or with the OpenAPI client generator (no contract change).
- No feature flag.

The branch can be implemented, built, tested, and merged in a single small PR.
```