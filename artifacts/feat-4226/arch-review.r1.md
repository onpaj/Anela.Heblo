# Architecture Review: Relocate OutlookEventImportMapper from UseCases to Services

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal-namespace/file relocation inside the Marketing module's Application layer — no behavior, contract, controller, or persistence changes. It aligns with `docs/architecture/filesystem.md`'s stated split: `UseCases/` holds MediatR handlers (one folder per verb+entity operation), `Services/` holds "domain services and business logic" that those handlers (or other services) depend on. The finding correctly identifies a dependency-direction smell: a Services-layer class (`MarketingCalendarSyncService`) reaching into a UseCases-layer subfolder (`ImportFromOutlook/`) for a helper that no handler ever touches. Fixing it is low-risk, mechanical, and strictly improves conformance to the documented layering — no open design questions of substance, only the two the spec flagged, both resolved below by inspecting the actual folder contents.

## Proposed Architecture

### Component Overview
No new components. Existing call graph, corrected:

```
Before (inverted dependency):
  ImportFromOutlookHandler (UseCases/ImportFromOutlook/)
        │  calls
        ▼
  IMarketingCalendarSyncService  ──────────────┐
        │ (Services/)                          │ uses (WRONG DIRECTION)
        ▼                                      ▼
  MarketingCalendarSyncService.SyncAsync()  OutlookEventImportMapper
                                             (UseCases/ImportFromOutlook/) ← misplaced

After (corrected):
  ImportFromOutlookHandler (UseCases/ImportFromOutlook/)
        │  calls
        ▼
  IMarketingCalendarSyncService
        │ (Services/)
        ▼
  MarketingCalendarSyncService.SyncAsync()
        │  uses (same-layer, same-namespace)
        ▼
  OutlookEventImportMapper (Services/) ← relocated
```

### Key Design Decisions

#### Decision 1: Target folder — `Services/` vs `Infrastructure/`
**Options considered:**
- `Services/`: matches the spec's assumption and the sole consumer's location.
- `Infrastructure/`: the spec flagged this as architect-confirmable against actual folder contents.

**Chosen approach:** `Services/`.

**Rationale:** I inspected both directories directly:
- `Features/Marketing/Infrastructure/` currently contains exactly `OutlookEventDto.cs` (a plain external-system DTO) and a `Jobs/` subfolder (scheduled job triggers). It holds no business-logic/mapping code — consistent with `filesystem.md`'s definition of `Infrastructure/` as "feature-specific infrastructure" (schedulers, feature flags, exceptions, external I/O), not domain logic.
- `Features/Marketing/Services/` already contains `MarketingCategoryMapper.cs` — a mapper with materially the same shape as `OutlookEventImportMapper` (pure category-name ↔ `MarketingActionType` translation logic, no I/O), registered as a `Services/`-layer component and consumed by the same `MarketingCalendarSyncService`. `OutlookEventImportMapper` (subject/body → `MarketingAction` field mapping, HTML stripping, change detection) is the same kind of thing: business-logic mapping, not infrastructure/I/O. Placing it beside `MarketingCategoryMapper` in `Services/` is the closest analogous precedent in this exact module, not just filesystem.md's general guidance.

This resolves Open Question #1 definitively: **`Services/`**, matching the spec's original assumption.

#### Decision 2: Rename the class as part of the move?
**Options considered:**
- Rename to something like `MarketingActionMapper` or `OutlookMappingService` to shed the "UseCases-sounding" `ImportFromOutlook`-scoped name.
- Keep the name unchanged, move namespace/file only.

**Chosen approach:** No rename — keep `OutlookEventImportMapper`.

**Rationale:** The name describes what it does (maps Outlook events for import) regardless of which layer folder it lives in; it's still accurate and unambiguous next to `MarketingCategoryMapper` in `Services/`. Renaming would touch call sites in `MarketingCalendarSyncService` (3 call sites) for cosmetic reasons only, expanding the diff beyond the stated "surgical, no rename" goal and CLAUDE.md's "surgical changes" rule. This resolves Open Question #2 definitively: **no rename**, matching the spec's assumption.

## Implementation Guidance

### Directory / Module Structure
- **Create:** `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` (moved content, see below).
- **Delete:** `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs`.
- **Leave untouched:** `ImportFromOutlookHandler.cs` (FR-4, confirmed — it never references the mapper, only `IMarketingCalendarSyncService`), `ImportFromOutlookRequest.cs`/`Response.cs` if present, and everything else in `UseCases/ImportFromOutlook/`.

### Interfaces and Contracts
No interface or contract changes. The class stays `internal static`; its three members (`BuildAction`, `HasChanges`, `ApplyChanges`) keep identical signatures. Internal visibility continues to work at the new location: `InternalsVisibleTo("Anela.Heblo.Tests")` is declared assembly-wide in `Anela.Heblo.Application`'s `AssemblyInfo.cs`/csproj, not namespace-scoped, so no visibility change is needed regardless of which internal namespace the class moves to.

Concrete diff shape:

```csharp
// Services/OutlookEventImportMapper.cs
using System;
using System.Text.RegularExpressions;
using Anela.Heblo.Application.Features.Marketing.Infrastructure; // OutlookEventDto stays here — unaffected
using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Services   // was: ...Marketing.UseCases.ImportFromOutlook
{
    internal static class OutlookEventImportMapper
    {
        // ... members unchanged ...
    }
}
```

Note: drop the `using Anela.Heblo.Application.Features.Marketing.Services;` line from the moved file (it referenced its own future namespace and becomes a self-reference/no-op — remove it as part of the namespace change, it was only needed because `SyncActor` lived in `Services` while the mapper lived in `UseCases`).

```csharp
// Services/MarketingCalendarSyncService.cs — remove this using:
- using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
```
The three call sites (`OutlookEventImportMapper.HasChanges(...)` at line 131, `.ApplyChanges(...)` at line 150, `.BuildAction(...)` at line 166) require **no edit** — same-namespace resolution takes over once both files declare `Anela.Heblo.Application.Features.Marketing.Services`.

### Data Flow
Unchanged. `ImportFromOutlookHandler.Handle()` still calls `_syncService.SyncAsync(...)`; `MarketingCalendarSyncService.SyncAsync()` still internally calls the mapper's three static methods per Outlook event during diffing/apply. Only the compile-time location of the mapper code changes; the runtime call graph and every method body are byte-identical.

### Test impact (resolves FR-5's premise)
Full-worktree search for `OutlookEventImportMapper` found **no test file references the class or its namespace** — not `ImportFromOutlookHandlerTests.cs`, not `MarketingCalendarSyncServiceTests.cs`, nor any other Marketing test. All existing tests exercise the mapper only indirectly through `MarketingCalendarSyncService`'s public `SyncAsync` behavior (black-box), and those tests reference no namespace of the mapper. **FR-5's "update tests referencing the old namespace" is a no-op for this codebase as it stands today** — flag this in the spec (see amendments below) so the implementer doesn't spend time hunting for a test file that doesn't exist. `dotnet build` + the full Marketing test suite passing is sufficient proof of correctness; no test file edits are expected.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed `using` cleanup leaves an unused/duplicate using in `MarketingCalendarSyncService.cs` | Low | `dotnet format`/analyzer will flag unused usings; run `dotnet build` with warnings-as-errors if configured, or just `dotnet format` per CLAUDE.md's validation step. |
| Someone assumes a test needs updating per FR-5 and creates one unnecessarily, expanding the diff | Low | Amendment below clarifies no test currently references the old namespace — the "surgical changes" rule means don't add new test files for this PR. |
| Merge/rebase noise if another in-flight branch touches `ImportFromOutlook/OutlookEventImportMapper.cs` | Low | Single-developer repo per CLAUDE.md project facts; land this PR promptly, low collision risk. |

## Specification Amendments
1. **Open Question 1 resolved:** target is `Services/` (not `Infrastructure/`). `Infrastructure/` in this module holds only `OutlookEventDto.cs` and job scheduling (`Jobs/`) — no business-logic mappers — while `Services/` already hosts the directly analogous `MarketingCategoryMapper`. Amend FR-1/FR-2 to state this as settled, not open.
2. **Open Question 2 resolved:** no rename. Amend to state this as settled.
3. **FR-5 correction:** no test currently references `OutlookEventImportMapper`'s namespace (verified via full-repo grep). Change FR-5 to: "Preserve full functional behavior; method bodies unchanged aside from namespace. No test file currently references the mapper's namespace directly, so no test changes are expected — confirm via `grep -r OutlookEventImportMapper backend/test/` before closing the PR, and update any that do surface."
4. **Additional cleanup this move surfaces:** the moved file currently has a `using Anela.Heblo.Application.Features.Marketing.Services;` directive (needed today because `SyncActor` lives in `Services` while the mapper lived in `UseCases`). Once the mapper moves into `Services`, this using becomes a same-namespace no-op and should be deleted as part of FR-2's namespace change (not a separate requirement, just a corollary the implementer should not miss).

## Prerequisites
None. No migrations, config, or infrastructure changes needed — this is a same-assembly, same-project file/namespace move with a one-line `using` removal in the consumer. Standard validation per CLAUDE.md applies: `dotnet build` + `dotnet format`, then run the Marketing test suite (`ImportFromOutlookHandlerTests`, `MarketingCalendarSyncServiceTests`, `OutlookCalendarSyncServiceTests`, `OutlookCalendarSyncServiceTokenTests`) to confirm no regression.
