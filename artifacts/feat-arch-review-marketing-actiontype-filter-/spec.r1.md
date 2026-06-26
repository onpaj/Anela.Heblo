# Specification: Marketing Action Type Filter

## Summary
Wire up the existing but dead `ActionType` filter on `GetMarketingActions` so users can filter the marketing actions list by their type (e.g., campaign, email, social). The repository and criteria infrastructure already supports the filter — only the request DTO, handler mapping, and frontend query parameter are missing.

## Background
A daily architecture review on 2026-05-17 flagged that `MarketingActionQueryCriteria.ActionType` and the corresponding repository branch in `MarketingActionRepository.GetPagedAsync` are dead code: nothing populates the criteria field because `GetMarketingActionsRequest` has no `ActionType` property and `GetMarketingActionsHandler` never maps it.

Two resolutions are possible: complete the wiring or delete the dead branch. This spec assumes the **complete-the-wiring** path because:
1. The data model supports it and the partial implementation suggests it was the original intent.
2. Filtering marketing actions by type is a natural, user-visible capability that aids triage in a list of mixed action types.
3. Removing the infrastructure is trivially reversible if the product decision later changes; reinstating a removed feature is more costly.

If product disagrees, see Open Question OQ-1 for the dead-code-removal alternative.

## Functional Requirements

### FR-1: Add `ActionType` to the request contract
Add an optional `MarketingActionType? ActionType` property to `GetMarketingActionsRequest` (in `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/`).

**Acceptance criteria:**
- Property is nullable; absence means "no filter" (matches existing optional-filter convention on the request).
- Property uses the existing `MarketingActionType` enum — no new types introduced.
- Generated TypeScript client exposes the new query parameter after `npm run build`.

### FR-2: Map `ActionType` in the handler
Update `GetMarketingActionsHandler` to copy `request.ActionType` onto the `MarketingActionQueryCriteria` it builds, alongside the other existing field mappings (lines 24–35 in the current handler).

**Acceptance criteria:**
- Handler maps `ActionType` unconditionally (the nullable value carries the "no filter" signal).
- When `request.ActionType` is `null`, the repository returns the same results as today (regression-safe).
- When `request.ActionType` has a value, the repository returns only actions whose `ActionType` equals the supplied value.

### FR-3: Expose the filter as an HTTP query parameter
The MVC controller endpoint that serves `GetMarketingActions` must accept `actionType` as an optional query string parameter and bind it to the request DTO. No new endpoint or route is required.

**Acceptance criteria:**
- `GET /api/marketing/actions?actionType={value}` returns only matching rows.
- Omitting the parameter returns the current unfiltered behaviour.
- Invalid enum values produce a 400 with a clear error message (use default ASP.NET Core model binding behaviour).

### FR-4: Frontend filter control
The Marketing Actions list page must expose an "Action Type" filter (dropdown) populated from `MarketingActionType` enum values, with an "All" / cleared option.

**Acceptance criteria:**
- Selecting a value triggers a refetch with the `actionType` query parameter.
- Clearing the selection refetches without the parameter.
- The filter state participates in the existing list-page filter pattern (URL state, persistence, reset button) consistent with sibling filters on the same page.
- Empty result state renders the existing "no results" UI — no new empty-state design needed.

### FR-5: Preserve repository behaviour
No change is needed in `MarketingActionRepository.GetPagedAsync`. The existing `if (criteria.ActionType.HasValue)` branch is the activation point.

**Acceptance criteria:**
- No edit to `MarketingActionRepository.cs` for the filter logic itself.
- Existing repository unit/integration tests continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
- The added `WHERE ActionType = @p` predicate must not measurably degrade query latency for the existing paged-list endpoint (target: <10ms additional overhead on a representative dataset).
- If the `MarketingAction.ActionType` column is not already indexed and the dataset is large enough to matter, add a non-clustered index — see Open Question OQ-2.

### NFR-2: Security & Authorization
- No new authorization rules. The filter is read-only and inherits the existing authorization on `GetMarketingActions`.
- Input is bound through the typed enum, so SQL injection is not a concern.

### NFR-3: Backwards compatibility
- Existing API clients that do not send `actionType` must observe identical behaviour to today.
- No database migration required (the column exists).

## Data Model
No schema change. The `MarketingAction` entity already carries an `ActionType` column of type `MarketingActionType` (enum). The filter operates on that existing column.

## API / Interface Design

**Endpoint (unchanged route, new optional parameter):**
```
GET /api/marketing/actions
  ?actionType={MarketingActionType}   // NEW, optional
  &{existing filters...}
  &page={int}
  &pageSize={int}
```

**Request DTO (additive change):**
```csharp
public class GetMarketingActionsRequest
{
    // ...existing properties...
    public MarketingActionType? ActionType { get; set; }
}
```

**Handler mapping (additive change):**
```csharp
var criteria = new MarketingActionQueryCriteria
{
    // ...existing mappings...
    ActionType = request.ActionType,
};
```

**Frontend:** the action-type dropdown lives in the existing Marketing Actions list filter bar alongside the current filters. No new page or modal.

## Dependencies
- `MarketingActionType` enum (already defined in the Marketing domain).
- OpenAPI client generation pipeline (auto-runs on build).
- Existing list-page filter framework on the frontend (URL state + refetch hook).

## Out of Scope
- Multi-select filtering (filter accepts one value at a time). If multi-select is later needed, the request property becomes `IReadOnlyCollection<MarketingActionType>` and the repository switches to a `Contains` predicate — out of scope here.
- Other dead-code findings in the Marketing module; this spec addresses only the `ActionType` filter.
- Changing the existing pagination, sorting, or other filter behaviours.
- Reporting, analytics, or saved-filter presets.

## Open Questions

### OQ-1: Confirm "add the filter" over "delete the dead code"
The brief presents both options. This spec assumes adding the filter. Product should confirm — if the answer is "delete instead", the spec collapses to: remove `ActionType` from `MarketingActionQueryCriteria.cs` and the `if (criteria.ActionType.HasValue)` block in `MarketingActionRepository.GetPagedAsync`, plus any now-unused tests.

### OQ-2: Index on `MarketingAction.ActionType`
Should we add a non-clustered index on the `ActionType` column? Depends on current and projected row counts in the marketing actions table. If the table is small (< ~10k rows) or rarely filtered, no index is needed. Confirm whether a manual migration should be drafted as part of this work.

### OQ-3: Filter UI placement and labelling
The spec assumes the dropdown sits in the existing filter bar with label "Action Type" (English) / "Typ akce" (Czech). Confirm Czech translation and exact label, and confirm placement order relative to existing filters.

## Status: HAS_QUESTIONS