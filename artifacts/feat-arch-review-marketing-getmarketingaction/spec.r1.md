# Specification: Extract MarketingAction → MarketingActionDto mapping out of GetMarketingActionsHandler

## Summary
Move the `MarketingAction → MarketingActionDto` projection currently living on `GetMarketingActionsHandler` (as `internal static MapToDto`) to a single, neutral location so that both `GetMarketingActionHandler` (single-item) and `GetMarketingActionsHandler` (list) consume it without any handler-to-handler coupling. This is a pure structural refactor — no behaviour, request shape, response shape, or wire contract changes.

## Background
`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs:36` currently builds its response by calling `GetMarketingActionsHandler.MapToDto(action)`. The mapping method (`GetMarketingActionsHandler.cs:52–80`) is a pure projection — no list/paging semantics — yet it is owned by the list use case.

This creates three problems:
- **Single Responsibility violation:** the list handler now owns mapping logic shared by another use case.
- **Cross-use-case coupling:** renaming, splitting, or moving `GetMarketingActionsHandler` silently breaks `GetMarketingActionHandler`.
- **Poor discoverability:** a reader asking "how is `MarketingActionDto` populated?" must know to look inside the list handler rather than near the DTO definition.

The daily arch-review routine flagged this on 2026-05-26 with an explicit suggested fix: lift the mapping to a static factory method on `MarketingActionDto` itself (preferred) or to a `MarketingActionMappingExtensions` class in the `Contracts/` folder.

This refactor is consistent with the project rule that DTOs are classes, not records (`docs/architecture/development_guidelines.md`) — the DTO class remains; only a static factory method is added to it. The OpenAPI client generator is unaffected: it does not emit static methods into the generated TypeScript client.

## Functional Requirements

### FR-1: Add static factory `MarketingActionDto.FromEntity`
Add a `public static MarketingActionDto FromEntity(MarketingAction action)` method to `Anela.Heblo.Application.Features.Marketing.Contracts.MarketingActionDto`. The method body is a verbatim move of the current `GetMarketingActionsHandler.MapToDto` projection — every property assignment must produce identical output for the same input.

**Acceptance criteria:**
- `MarketingActionDto.FromEntity(action)` returns a new `MarketingActionDto` populated from the `MarketingAction` aggregate.
- For every existing field on `MarketingActionDto` (`Id`, `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `CreatedAt`, `ModifiedAt`, `CreatedByUserId`, `CreatedByUsername`, `ModifiedByUserId`, `ModifiedByUsername`, `AssociatedProducts`, `FolderLinks`, `OutlookSyncStatus`, `OutlookEventId`), the assigned value matches what `GetMarketingActionsHandler.MapToDto` produces today.
- `AssociatedProducts` is built from `action.ProductAssociations.Select(pa => pa.ProductCodePrefix).Distinct().ToList()` (same de-duplication as today).
- `FolderLinks` is built from `action.FolderLinks.Select(fl => new MarketingActionFolderLinkDto { FolderKey = fl.FolderKey, FolderType = fl.FolderType.ToString() }).ToList()`.
- `ActionType` and `OutlookSyncStatus` are projected via `.ToString()` on the source enums (identical to current behaviour).
- The method has no side effects, no logging, no DI dependencies; it is a pure projection.

### FR-2: Update `GetMarketingActionHandler` to use the new factory
Replace `GetMarketingActionsHandler.MapToDto(action)` in `GetMarketingActionHandler.cs:36` with `MarketingActionDto.FromEntity(action)`. Remove the now-unused `using Anela.Heblo.Application.Features.Marketing.UseCases.GetMarketingActions;` directive (line 5) if no other reference remains in the file.

**Acceptance criteria:**
- `GetMarketingActionHandler.cs` contains no reference to `GetMarketingActionsHandler` (type or members).
- `GET /api/marketing-actions/{id}` continues to return the same JSON body for the same stored entity (no field added, removed, renamed, or reformatted).
- The 404 path (`ErrorCodes.MarketingActionNotFound`) is unchanged.

### FR-3: Update `GetMarketingActionsHandler` to use the new factory and delete the old method
Replace the local `MapToDto` reference in `GetMarketingActionsHandler.cs:42` (`result.Items.Select(MapToDto).ToList()`) with `result.Items.Select(MarketingActionDto.FromEntity).ToList()`. Delete the `internal static MarketingActionDto MapToDto(MarketingAction action)` method (lines 52–80).

**Acceptance criteria:**
- `GetMarketingActionsHandler.cs` no longer declares `MapToDto`.
- `GET /api/marketing-actions` continues to return the same paged JSON body for the same query (item shape, total count, paging flags, ordering all unchanged).

### FR-4: No other call sites need updating
A repository-wide grep for `GetMarketingActionsHandler.MapToDto` must return zero matches after the change. The only two existing call sites are the two handlers covered by FR-2 and FR-3.

**Acceptance criteria:**
- `grep -R "GetMarketingActionsHandler.MapToDto" backend/` returns no matches.
- `grep -R "GetMarketingActionsHandler\." backend/` returns no matches outside of `GetMarketingActionsHandler.cs` itself and its test file (which references the type as `<GetMarketingActionsHandler>` for the `IRequestHandler` under test, not its members).

## Non-Functional Requirements

### NFR-1: Behaviour parity
Zero behavioural change. No new properties, no new validation, no logging additions, no enum reformatting, no allocation change visible to callers. The refactor is byte-identical at the API boundary.

### NFR-2: Build, format, and test gates
- `dotnet build` succeeds at solution root with no new warnings.
- `dotnet format` produces no diff.
- `dotnet test` passes for `Anela.Heblo.Tests`, including the existing `backend/test/Anela.Heblo.Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs` suite (which exercises the full handler path and therefore implicitly covers the projection).
- Frontend OpenAPI TypeScript client regenerates with **no diff** (`npm run build` does not change `frontend/src/api-client/`). Static methods do not appear in OpenAPI output, so the contract is preserved.

### NFR-3: Architectural fit
- `MarketingActionDto` (in `Application/Features/Marketing/Contracts/`) gains a dependency on `Anela.Heblo.Domain.Features.Marketing.MarketingAction`. Both types already live inside the `Anela.Heblo.Application` project (Domain is a project reference), so this introduces no new project-level reference — only a new namespace-level dependency direction (Contracts → Domain) inside the same assembly. This is acceptable per the brief, which explicitly approves placing the factory on the DTO.
- The DTO remains a `class` (not a `record`) per the project rule on OpenAPI generator compatibility.

### NFR-4: Test coverage
No new unit tests are required: the projection is covered end-to-end by the existing handler tests, which exercise it through `Handle(...)` and assert response shape. Adding a dedicated `MarketingActionDtoTests.FromEntity_*` suite is encouraged but optional — if added, it must use AAA structure and FluentAssertions per `~/.claude/rules/csharp-testing.md`.

## Data Model
No schema changes. No new entities. No new DTO fields.

Existing types touched (logical view only — no shape change):

- `Anela.Heblo.Application.Features.Marketing.Contracts.MarketingActionDto` — class; gains a `public static FromEntity(MarketingAction)` factory.
- `Anela.Heblo.Application.Features.Marketing.Contracts.MarketingActionFolderLinkDto` — unchanged; constructed inside `FromEntity`.
- `Anela.Heblo.Domain.Features.Marketing.MarketingAction` — read-only source; not modified.

## API / Interface Design
No public API change. Endpoints, request/response contracts, error codes, and HTTP status behaviour are unchanged:

- `GET /api/marketing-actions/{id}` → `GetMarketingActionResponse { Action: MarketingActionDto }`
- `GET /api/marketing-actions` → `GetMarketingActionsResponse { Actions: MarketingActionDto[], TotalCount, PageNumber, PageSize, TotalPages, HasNextPage, HasPreviousPage }`

Internal interface change (C# only, not part of any public contract):

- **Added:** `public static MarketingActionDto MarketingActionDto.FromEntity(MarketingAction action)`
- **Removed:** `internal static MarketingActionDto GetMarketingActionsHandler.MapToDto(MarketingAction action)`

## Dependencies
- **`MediatR`** — unchanged; handlers continue to implement `IRequestHandler<,>`.
- **`IMarketingActionRepository`** — unchanged.
- **Domain assembly** — already referenced by the Application project; the new factory simply reads from a Domain aggregate, introducing no new project reference.
- **OpenAPI client generation pipeline** — must regenerate cleanly (no diff expected).

No new NuGet packages, no new services to register in DI, no new configuration.

## Out of Scope
- Refactoring the **other** sibling-handler `MapToDto` call sites found in the same repo (e.g. `CreateLotHandler.MapToDto` referenced from `UpdateLotHandler` / `GetLotHandler` / `ListLotsHandler`; `CreateEansHandler.MapToDto` referenced from `GetEanByCodeHandler` / `ListEansHandler`). These are structurally analogous but are explicitly **not** part of this change. They may be addressed by future arch-review findings; mention them in the PR description but do not modify them here.
- Adding new fields to `MarketingActionDto` (e.g. a single-item view that exposes more data than the list view). The single/list shapes remain identical after this refactor.
- Replacing manual mapping with AutoMapper, Mapster, or any mapping library — out of scope.
- Changes to `MarketingActionFolderLinkDto`, error codes, controllers, or repository.
- Frontend changes (no contract change → no frontend change).
- E2E test additions or modifications.

## Open Questions
None. The brief explicitly approves placing the factory on `MarketingActionDto`; the alternative `MarketingActionMappingExtensions` class is documented above as the fallback if the architect later prefers to keep the DTO free of Domain references. Default chosen: factory method on the DTO.

## Status: COMPLETE