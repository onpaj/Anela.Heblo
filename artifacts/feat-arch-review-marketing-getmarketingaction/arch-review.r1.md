# Architecture Review: Extract MarketingAction → MarketingActionDto mapping out of GetMarketingActionsHandler

## Skip Design: true

Backend-only structural refactor. No UI components, screens, or visual changes — the wire contract is byte-identical at the API boundary, so the frontend is untouched.

## Architectural Fit Assessment

The change fits cleanly. Three points of grounding from the codebase:

1. **Same-assembly Contracts → Domain dependency already exists.** `Anela.Heblo.Application.Features.Marketing.Contracts/` already imports `Anela.Heblo.Domain.Features.Marketing` from `CreateMarketingActionRequest.cs`, `UpdateMarketingActionRequest.cs`, `GetMarketingActionsRequest.cs`, and `MarketingFolderLinkRequest.cs` (they reference `MarketingActionType`). Adding a `using Anela.Heblo.Domain.Features.Marketing;` to `MarketingActionDto.cs` introduces no new direction — it follows the existing convention in that folder.
2. **Vertical Slice Architecture supports it.** `docs/architecture/development_guidelines.md` mandates that DTOs live in `Contracts/` per feature and forbids shared/global DTO locations. The factory stays inside the same feature slice (`Marketing/Contracts/`), preserving slice cohesion. The current state — list-handler owning shared mapping — is the actual violation of "feature cohesion": single-item and list-item use cases are sibling slices, and the cross-handler reference cuts across them.
3. **Existing mapping convention is mixed.** The codebase has a precedent for a dedicated mapper class in `Features/Journal/Mapping/JournalEntryMapper.cs` (`public static JournalEntryDto ToDto(JournalEntry entry)`), and a more service-shaped `Features/Manufacture/Services/ManufactureAnalysisMapper.cs`. The brief explicitly approves the factory-on-DTO option; the spec defaults to it. I confirm this is the right call here (see Decision 1) but call out the existing alternatives so the developer is not surprised.

Single integration point: the two handlers. No DI, no controllers, no migrations, no contract change.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application (assembly)                              │
│                                                                 │
│  Features/Marketing/                                            │
│  ├── Contracts/                                                 │
│  │   ├── MarketingActionDto.cs   ◄── NEW: static FromEntity()   │
│  │   │     (data + static factory; pure projection)             │
│  │   └── MarketingActionFolderLinkDto.cs                        │
│  │                                                              │
│  └── UseCases/                                                  │
│      ├── GetMarketingAction/                                    │
│      │   └── GetMarketingActionHandler.cs                       │
│      │         └─ calls MarketingActionDto.FromEntity(action)   │
│      │                                                          │
│      └── GetMarketingActions/                                   │
│          └── GetMarketingActionsHandler.cs                      │
│                └─ result.Items.Select(MarketingActionDto.FromEntity)
│                (MapToDto removed)                               │
└─────────────────────────────────────────────────────────────────┘
                          ▼ reads
┌─────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Domain (assembly, already referenced)               │
│  Features/Marketing/MarketingAction.cs (read-only source)       │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Place the factory on the DTO (`MarketingActionDto.FromEntity`)
**Options considered:**
- (a) Static factory method on the DTO itself (`MarketingActionDto.FromEntity(action)`).
- (b) `MarketingActionMappingExtensions` extension-method class in `Marketing/Contracts/`.
- (c) Dedicated `MarketingActionMapper` static class in `Marketing/Mapping/` (mirrors `JournalEntryMapper`).

**Chosen approach:** (a) — `public static MarketingActionDto FromEntity(MarketingAction action)` on the DTO.

**Rationale:** The brief explicitly approves this option. Discoverability is the dominant concern raised by the arch-review finding ("a reader asking 'how is MarketingActionDto populated?' must know to look inside the list handler"); placing the factory beside the DTO's field declarations makes the mapping discoverable in the one place a reader would naturally land. The Contracts → Domain direction inside the same assembly is already established in this exact folder, so option (a) introduces no new architectural seam. The `JournalEntryMapper` pattern (option c) is valid but heavier and is best kept for cases with multiple DTO shapes (`ToDto` + `ToSearchDto`) — not warranted here.

#### Decision 2: Method name — `FromEntity` (not `ToDto`, `Map`, or `Create`)
**Options considered:** `FromEntity`, `ToDto`, `Map`, `Create`.

**Chosen approach:** `FromEntity` — matches the spec, reads naturally at the call site (`MarketingActionDto.FromEntity(action)` — "make a DTO from this entity").

**Rationale:** When the factory lives on the DTO type itself, `FromEntity` is the idiomatic .NET naming (cf. `XElement.Load`, `Guid.Parse`). `ToDto` would be the right name if the mapping lived on `MarketingAction` (e.g., as an extension), but `MarketingAction` is a Domain aggregate and must not depend on Application/Contracts.

#### Decision 3: Keep `MarketingActionDto` a `class`, not a `record`
**Options considered:** Convert DTO to a positional `record` while touching the file; keep it a `class`.

**Chosen approach:** Keep it a `class`.

**Rationale:** Project rule (`docs/architecture/development_guidelines.md` and `CLAUDE.md`): DTOs are classes, not records, because the OpenAPI/TypeScript client generator mishandles record parameter order. The DTO already has many init-style properties and is consumed by the generator. The refactor must not bait-and-switch this constraint.

#### Decision 4: No method-group simplification beyond `Select(MarketingActionDto.FromEntity)`
**Options considered:** Pass as method group `Select(MarketingActionDto.FromEntity)` vs. lambda `Select(a => MarketingActionDto.FromEntity(a))`.

**Chosen approach:** Method group (spec FR-3 specifies it). Equivalent runtime behaviour, zero behavioural risk, and reads cleanly.

## Implementation Guidance

### Directory / Module Structure
No new files. Three files touched:

- **`backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs`** — add `using System.Linq;` and `using Anela.Heblo.Domain.Features.Marketing;`; append `public static MarketingActionDto FromEntity(MarketingAction action) => new() { … }` with the exact projection currently in `GetMarketingActionsHandler.MapToDto`.
- **`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs`** — replace line 36 call with `MarketingActionDto.FromEntity(action)`; remove the now-unused `using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;` at line 5.
- **`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs`** — replace `Select(MapToDto)` at line 42 with `Select(MarketingActionDto.FromEntity)`; delete the `internal static MarketingActionDto MapToDto(MarketingAction action)` method (lines 52–80). The `using Anela.Heblo.Application.Features.Marketing.Contracts;` directive must remain (DTO reference at the call site).

### Interfaces and Contracts

Added (internal C# API, **not** part of the OpenAPI wire contract):

```csharp
// In Anela.Heblo.Application.Features.Marketing.Contracts
public class MarketingActionDto
{
    // existing properties unchanged …

    public static MarketingActionDto FromEntity(MarketingAction action) =>
        new()
        {
            Id = action.Id,
            Title = action.Title,
            Description = action.Description,
            ActionType = action.ActionType.ToString(),
            StartDate = action.StartDate,
            EndDate = action.EndDate,
            CreatedAt = action.CreatedAt,
            ModifiedAt = action.ModifiedAt,
            CreatedByUserId = action.CreatedByUserId,
            CreatedByUsername = action.CreatedByUsername,
            ModifiedByUserId = action.ModifiedByUserId,
            ModifiedByUsername = action.ModifiedByUsername,
            AssociatedProducts = action.ProductAssociations
                .Select(pa => pa.ProductCodePrefix)
                .Distinct()
                .ToList(),
            FolderLinks = action.FolderLinks
                .Select(fl => new MarketingActionFolderLinkDto
                {
                    FolderKey = fl.FolderKey,
                    FolderType = fl.FolderType.ToString(),
                })
                .ToList(),
            OutlookSyncStatus = action.OutlookSyncStatus.ToString(),
            OutlookEventId = action.OutlookEventId,
        };
}
```

Removed: `GetMarketingActionsHandler.MapToDto(MarketingAction)`.

OpenAPI/wire contract: unchanged. `GET /api/marketing-actions/{id}` and `GET /api/marketing-actions` respond with identical JSON. Static methods are not emitted to the TypeScript client.

### Data Flow

```
HTTP GET /api/marketing-actions/{id}
  → MarketingActionsController
  → MediatR → GetMarketingActionHandler.Handle
      → _repository.GetByIdAsync(id)               (Domain MarketingAction)
      → MarketingActionDto.FromEntity(action)      (NEW seam — pure projection)
      → GetMarketingActionResponse { Action = … }
  ← JSON response (unchanged shape)

HTTP GET /api/marketing-actions
  → MarketingActionsController
  → MediatR → GetMarketingActionsHandler.Handle
      → _repository.GetPagedAsync(criteria)        (PagedResult<MarketingAction>)
      → result.Items.Select(MarketingActionDto.FromEntity).ToList()
      → GetMarketingActionsResponse { Actions, paging metadata }
  ← JSON response (unchanged shape)
```

Single new seam: `MarketingActionDto.FromEntity`. Both flows converge on it; neither depends on the other.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Behavioural drift between old `MapToDto` and new `FromEntity` (typo, missed property, reordered LINQ) | HIGH | Move the projection verbatim — copy/paste, do not retype. Run the existing `GetMarketingActionsHandlerTests` suite which exercises the projection end-to-end; if any assertion regresses, the drift is caught before commit. |
| Stale `using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;` left behind in `GetMarketingActionHandler.cs` triggers a `dotnet format` diff or a CS analyzer warning | LOW | Remove the directive in the same edit (FR-2 acceptance). Verify with `dotnet format --verify-no-changes`. |
| OpenAPI/TypeScript client regenerates with a diff (e.g., static method leaks into emitted client) | LOW | NSwag/OpenAPI generators do not emit static methods from DTOs; verified by spec NFR-2. Validate locally by running the frontend build and confirming `git status frontend/src/api-client/` is clean. |
| Future divergence between single-item and list views silently splits the projection again | MEDIUM | If a single-item view later needs additional fields, the team must consciously decide between (a) extending `MarketingActionDto` (both endpoints expose the field) or (b) introducing a distinct `MarketingActionDetailDto` with its own `FromEntity`. This is now an explicit design decision rather than an accidental fork via `if/else` inside `MapToDto`. Document this in the PR description. |
| Reviewer requests applying the same refactor to the other flagged sibling-`MapToDto` sites (CreateLotHandler, CreateEansHandler) inside this PR | LOW | Spec Out-of-Scope explicitly excludes them. Mention them in the PR description as follow-up arch-review candidates; do not modify in this PR. |
| New `using Anela.Heblo.Domain.Features.Marketing;` in `MarketingActionDto.cs` is perceived as breaking Clean Architecture (Application → Domain) | LOW | The reference is Application-internal, in the same assembly, and the direction (Application/Contracts → Domain) is already the established direction across the codebase (Application depends on Domain). Multiple sibling files in the same `Marketing/Contracts/` folder already import Domain. Note this in the PR description if reviewer raises it. |

## Specification Amendments

The spec is implementation-ready. Two minor clarifications worth folding in:

1. **`using System.Linq;` is required in `MarketingActionDto.cs`** — the projection uses `.Select(...)`, `.Distinct()`, `.ToList()`. The current DTO file does not import `System.Linq`. Spec FR-1 should call this out explicitly so the developer doesn't miss it during the move.
2. **`AssociatedProducts` field-level acceptance criterion** — the spec correctly preserves `Distinct()` semantics. Note that the source `ProductAssociations` collection is already deduplicated by the domain method `MarketingAction.AssociateWithProduct` (line 78–79: it skips duplicates), so `Distinct()` is defensive and the parity guarantee holds even if a future code path adds raw associations. Keep `Distinct()` to preserve byte-identical output (NFR-1).

Everything else (FR-2, FR-3, FR-4, NFRs, Out-of-Scope) is correctly scoped.

## Prerequisites

None. No migrations, no config, no DI registrations, no infrastructure. The refactor is self-contained inside the `Marketing` feature slice of `Anela.Heblo.Application`. Required validation gates (`dotnet build`, `dotnet format`, `dotnet test`, frontend `npm run build` for OpenAPI no-diff) are already in `CLAUDE.md` and the spec's NFR-2 — no new tooling needed.