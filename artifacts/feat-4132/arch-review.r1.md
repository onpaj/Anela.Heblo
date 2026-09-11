# Architecture Review: Move `OutlookEventDto` (Microsoft Graph schema) from Marketing `Services/` to `Infrastructure/`

## Skip Design: true

Backend-only file relocation and namespace change. No new or changed UI components, no visual/layout decisions, no API contract surface changes — nothing for a designer to specify.

## Architectural Fit Assessment

This is a direct application of the documented placement rule in `docs/architecture/filesystem.md`: `Features/{Feature}/Services/` is for "Domain services and business logic," `Features/{Feature}/Infrastructure/` is for "Feature-specific infrastructure." `OutlookEventDto`/`GraphEventBody`/`GraphEventDateTime` are pure Microsoft Graph wire-format schemas (`[JsonPropertyName]`-annotated, Graph-specific date parsing) with zero business logic — they belong in `Infrastructure/`, which already exists for this module (currently holding only `Jobs/MarketingCalendarSyncJob.cs`). 28 other feature modules already separate `Services/` from `Infrastructure/` this way, so this is not a new pattern — it's correcting a one-off placement drift in Marketing.

I independently re-verified the spec's consumer inventory against source (repo-wide grep for `OutlookEventDto`, `GraphEventBody`, `GraphEventDateTime`) rather than trusting the original triaging finding's claim of "two existing usages." The finding under-counted: **seven** files reference these types by name and need a `using`-directive change, not two. The spec (FR-1–FR-9) already reflects the corrected, verified inventory — see **Specification Amendments** below for the one clarification needed on top of it (git mechanics), and confirmation that no further amendment is required.

Also verified: `IOutlookCalendarSync` correctly stays in `Services/` — it's the module-boundary contract interface (consumed across the `Anela.Heblo.Adapters.Microsoft365` project boundary via `ProjectReference`), which is exactly the kind of type `Services/` is documented to hold. Only the DTO type it references moves.

## Proposed Architecture

### Component Overview

```
Application/Features/Marketing/
├── Services/                                    (domain services — unchanged in kind)
│   ├── IOutlookCalendarSync.cs                   ← using updated only
│   ├── NoOpOutlookCalendarSync.cs                ← using updated only
│   ├── IMarketingCalendarSyncService.cs
│   ├── MarketingCalendarSyncService.cs           ← NOT touched (no direct type reference)
│   ├── IMarketingCategoryMapper.cs / MarketingCategoryMapper.cs
│   ├── SyncActor.cs
│   └── OutlookCalendarSyncException.cs
│
├── Infrastructure/                               (feature infrastructure)
│   ├── Jobs/MarketingCalendarSyncJob.cs          (existing, untouched)
│   └── OutlookEventDto.cs                        ← MOVED HERE (was in Services/)
│       (OutlookEventDto, GraphEventBody, GraphEventDateTime)
│
└── UseCases/ImportFromOutlook/
    └── OutlookEventImportMapper.cs               ← using added

Adapters/Anela.Heblo.Adapters.Microsoft365/        (implements IOutlookCalendarSync;
│                                                     ProjectReference → Application)
├── OutlookCalendarSyncService.cs                 ← using added
└── OutlookInternalDtos.cs                        ← using swapped (only Services symbol was OutlookEventDto)

Tests/Features/Marketing/
├── ImportFromOutlookHandlerTests.cs              ← using added
└── Services/MarketingCalendarSyncServiceTests.cs ← using added
```

Dependency direction is unaffected: `Anela.Heblo.Adapters.Microsoft365` still references `Anela.Heblo.Application` only, never the reverse. `OutlookEventDto` moving to `Infrastructure/` (still inside the Application project) keeps it upstream of the adapter that deserializes into it — this is precisely why it cannot move into the `Anela.Heblo.Adapters.Microsoft365` project itself (see Decision 2).

### Key Design Decisions

#### Decision 1: Flat `Infrastructure/OutlookEventDto.cs` vs. `Infrastructure/Graph/OutlookEventDto.cs` subfolder
**Options considered:**
- (A) Flat file directly under `Infrastructure/`, as the issue's primary suggestion proposes.
- (B) New `Infrastructure/Graph/` subfolder, as the issue's parenthetical alternative proposes.

**Chosen approach:** (A).

**Rationale:** `Infrastructure/` currently holds exactly one subfolder, `Jobs/`, for a genuinely distinct concern (Hangfire recurring jobs). A single 54-line file with three related classes doesn't warrant a new subfolder tier — flat placement matches how most other modules' `Infrastructure/` folders hold adapter/DTO files directly (e.g., `KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs`). If more Graph-specific infrastructure accumulates later (e.g., a Graph client wrapper), introducing `Graph/` then is a trivial follow-up with no cost paid now.

#### Decision 2: Keep `OutlookEventDto` in the Application project vs. move it into `Anela.Heblo.Adapters.Microsoft365`
**Options considered:**
- (A) `Features/Marketing/Infrastructure/` within `Anela.Heblo.Application` (spec's choice).
- (B) Move into `Anela.Heblo.Adapters.Microsoft365`, alongside `OutlookInternalDtos.cs`, since that's where the actual Graph HTTP calls and JSON deserialization happen.

**Chosen approach:** (A).

**Rationale:** (B) is superficially tempting — `OutlookInternalDtos.cs` already lives in the adapter project, and the live Graph deserialization happens there. But `IOutlookCalendarSync` (the module-boundary contract) is and must remain in the Application-layer `Services/` folder, and its `ListEventsAsync`/`GetEventAsync` signatures return `OutlookEventDto`. `Anela.Heblo.Adapters.Microsoft365` has a `ProjectReference` **to** `Anela.Heblo.Application`, never the reverse (verified in `Anela.Heblo.Adapters.Microsoft365.csproj`). Moving `OutlookEventDto` into the adapter project would force the Application layer to reference the Adapters layer to declare its own interface's return type — inverting Clean Architecture's dependency direction. (A) is the only option that doesn't break the build's layering.

## Implementation Guidance

### Directory / Module Structure

One file move (`git mv` preserves history and is the correct mechanism — a delete+recreate would lose blame history for no reason):
- `Application/Features/Marketing/Services/OutlookEventDto.cs` → `Application/Features/Marketing/Infrastructure/OutlookEventDto.cs` — namespace line only changes, from `Anela.Heblo.Application.Features.Marketing.Services` to `Anela.Heblo.Application.Features.Marketing.Infrastructure`.

Six `using`-directive-only edits (no other line changes in any of them):
- `Application/Features/Marketing/Services/IOutlookCalendarSync.cs` — add new `using`.
- `Application/Features/Marketing/Services/NoOpOutlookCalendarSync.cs` — add new `using`.
- `Application/Features/Marketing/UseCases/ImportFromOutlook/OutlookEventImportMapper.cs` — add new `using` (keep existing `Services` using for `SyncActor`).
- `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — add new `using` (keep existing `Services` using for `IOutlookCalendarSync`/`IMarketingCategoryMapper`/`OutlookCalendarSyncException`).
- `Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookInternalDtos.cs` — **swap** the existing `Services` using for the new one (no other `Services` symbol used here).
- `test/Anela.Heblo.Tests/Features/Marketing/ImportFromOutlookHandlerTests.cs` — add new `using` (keep existing `Services` using for `IOutlookCalendarSync`/`IMarketingCategoryMapper`).
- `test/Anela.Heblo.Tests/Features/Marketing/Services/MarketingCalendarSyncServiceTests.cs` — add new `using` (keep existing `Services` using for `SyncActor`/`IOutlookCalendarSync`/`IMarketingCategoryMapper`/`MarketingCalendarSyncService`).

No `.csproj` edits. No DI registration changes (`OutlookEventDto` etc. are POCOs, never registered in `MarketingModule.cs`). No `ModuleBoundariesTests.cs` changes — grep confirms no existing rule references Marketing.

### Interfaces and Contracts

`IOutlookCalendarSync` keeps its exact signature:
```csharp
public interface IOutlookCalendarSync
{
    Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct);
    Task UpdateEventAsync(MarketingAction action, CancellationToken ct);
    Task DeleteEventAsync(string outlookEventId, CancellationToken ct);
    Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct);
}
```
Only the resolution of `OutlookEventDto` changes (via the new `using`), not the signature text itself.

### Data Flow
Unchanged. Graph HTTP response → `JsonSerializer.DeserializeAsync<OutlookEventDto>` (or `GraphEventCollection` for list responses) in `OutlookCalendarSyncService` (adapter) → `IOutlookCalendarSync` returns the same `OutlookEventDto` instances → `MarketingCalendarSyncService` (via inferred typing, never names the type) → `OutlookEventImportMapper.BuildAction`/`HasChanges`/`ApplyChanges` read `.Subject`, `.BodyText`, `.StartUtc`, `.EndUtc`, `.Id`. Every step in this chain is unaffected in behavior; only the compile-time namespace resolution changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A missed call site elsewhere in the repo still resolves `OutlookEventDto` against the old `Services` namespace and fails to compile | Low | The repo-wide grep performed during this review (and independently during spec authoring) found exactly the 8 files listed (1 declaration + 7 consumers) — no others. `dotnet build` after the move immediately surfaces any survivor as a `CS0246` (type not found). |
| `Infrastructure/OutlookInternalDtos.cs`'s using-swap (not add) is the one file in this set where the wrong edit (adding instead of swapping) would leave a now-unused `using Anela.Heblo.Application.Features.Marketing.Services;` and trip `dotnet format`/analyzer unused-using warnings | Low | Explicitly called out in spec FR-6 and here — swap, don't add, for this file only. |
| `git mv` not used, so the file move loses history (shows as delete+add in `git log --follow` without `--follow`, or complicates review) | Low | Explicit implementation guidance above: use `git mv Services/OutlookEventDto.cs Infrastructure/OutlookEventDto.cs`, then edit the namespace line in place. |
| Both `Services/` and `Infrastructure/` folders end up imported side-by-side in five files, superficially suggesting a bigger dependency than exists | Negligible | This is the correct and minimal outcome — those five files genuinely use symbols from both namespaces (an interface plus a DTO, or a domain type plus a DTO). No further action needed. |

## Specification Amendments

1. **Add explicit `git mv` instruction to FR-1.** The spec's FR-1 describes the end state (file relocated, namespace changed) but doesn't mandate the git mechanism. Amend FR-1's acceptance criteria to require `git mv` (or an equivalent rename-preserving operation) rather than delete+recreate, so `git log --follow` and PR review show a rename, not a delete+add. This is the only correction — the spec's technical content (the eight-file inventory, the `Services`-using-must-stay list, the `OutlookInternalDtos.cs` swap-not-add) is accurate and matches what I independently re-verified against source.

No other amendments. `spec.r1.md` FR-1 through FR-9 are complete and technically correct against current source.

## Prerequisites

None. No migrations, no config, no infrastructure changes, no NSwag/OpenAPI regeneration (these types never cross the controller/HTTP boundary), no new project references. Implementation can start immediately.
