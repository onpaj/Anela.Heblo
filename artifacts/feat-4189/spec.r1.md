# Specification: Relocate or de-duplicate MarketingActionQueryCriteria

## Summary
An arch-review finding states that `MarketingActionQueryCriteria` (Domain) carries only paging/search/filter concerns and duplicates `GetMarketingActionsRequest` (Application) field for field. The handler maps nine fields with no transformation. The goal is to remove the layering smell and the mapping boilerplate.

## Background
Verified in code:
- `Domain/Features/Marketing/MarketingActionQueryCriteria.cs` has 10 properties (PageNumber, PageSize, SearchTerm, ActionType, 4 date bounds, ProductCodePrefix, IncludeDeleted). The brief says "nine"/"six date+product filters"; IncludeDeleted is the tenth.
- `IMarketingActionRepository.GetPagedAsync(MarketingActionQueryCriteria, ct)` is in Domain; implemented in `Persistence/Marketing/MarketingActionRepository.cs`.
- Project references: Application -> Domain, Persistence, Xcc. Persistence -> Domain, Xcc. Domain -> Xcc. Neither Domain nor Persistence references Application.
- `PagedResult<T>` (with PageNumber/PageSize) already lives in Xcc and is returned by the Domain repository interface, so paging is already a cross-layer concept.
- Tests referencing the class: `Tests/Repositories/MarketingActionRepositoryGetPagedTests.cs`, `Tests/Application/Marketing/GetMarketingActionsHandlerTests.cs`.

## Functional Requirements

### FR-1: Suggested fix "move to Application/Contracts" is not viable
Moving the class to Application would require Domain (the interface) and Persistence (the implementation) to reference Application, which inverts the dependency direction and creates a cycle (Application already references both). Likewise "pass `GetMarketingActionsRequest` directly to the repository" is not viable (it is an `IRequest` MediatR type in Application).
**Acceptance criteria:**
- No project reference from Domain or Persistence to Application is added.

### FR-2: Relocate criteria to a layer both sides can reference
Move the class out of the `Domain.Features.Marketing` entity namespace into Xcc next to `PagedResult<T>` (namespace `Anela.Heblo.Xcc.Persistance`), name unchanged, as a persistence query-criteria type. Domain, Persistence and Application already reference Xcc.
**Acceptance criteria:**
- Class compiles in Xcc; `MarketingActionType` (a Domain enum) is the blocker: see Open Questions.
- `IMarketingActionRepository`, `MarketingActionRepository`, handler and both test files compile against the new location.
- No behaviour change; existing tests pass.

### FR-3: Keep handler mapping (no merge)
The Request is the OpenAPI contract (generated clients); merging would couple API shape to persistence. The handler mapping stays.

## Non-Functional Requirements
- NFR-1: No change to the OpenAPI schema (criteria is not exposed). Verify generated client is unchanged.
- NFR-2: `dotnet build` and `dotnet format` clean.

## Data Model
No change.

## API / Interface Design
No HTTP change. Only the CLR namespace of `MarketingActionQueryCriteria` changes (or, in the fallback, nothing).

## Dependencies
Xcc project; Domain's `MarketingActionType` enum.

## Out of Scope
Merging request and criteria; changing filter set; other modules' criteria.

## Open Questions
- Xcc cannot reference Domain (Domain references Xcc), but the criteria uses `MarketingActionType` from Domain. Moving to Xcc is therefore not possible without moving/duplicating the enum. Assumption: the relocation is judged not worth it; the recommended outcome is to keep the class in Domain, document the rationale, and treat the finding as a documented non-fix. Needs maintainer confirmation.

## Status: HAS_QUESTIONS
