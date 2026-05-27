# Specification: Marketing Action Type Filter

## Summary
Wire up the existing but dead `ActionType` filter on `GetMarketingActions` so users can filter the marketing actions list by type (e.g., Sociální sítě, Blog, Newsletter, PR, Událost, Meeting). The repository, criteria infrastructure, and supporting DB index already exist — only the request DTO, handler mapping, and frontend filter control need to be added.

## Background
A daily architecture review on 2026-05-17 flagged that `MarketingActionQueryCriteria.ActionType` and the corresponding repository branch in `MarketingActionRepository.GetPagedAsync` are dead code: nothing populates the criteria field because `GetMarketingActionsRequest` has no `ActionType` property and `GetMarketingActionsHandler` never maps it.

Product has confirmed the **complete-the-wiring** resolution (not dead-code removal). Three pieces of evidence support this:
1. A non-clustered DB index `IX_MarketingActions_ActionType` already exists in migration `20260424095051_AddMarketingCalendar.cs:99–102` — it would not have been created if the filter were unintended.
2. `MarketingActionType` is a small, semantically meaningful enum (`SocialMedia`, `Blog`, `Newsletter`, `PR`, `Event`, `Meeting`) and the frontend already renders typed badges/labels per type in `MarketingActionGrid.tsx:20–36`, so filtering by type is a natural user-visible capability with zero new domain modelling.
3. The existing list-page filter bar (`MarketingActionFilters.tsx`) is the obvious host for the new control, making this a low-risk additive change.

## Functional Requirements

### FR-1: Add `ActionType` to the request contract
Add an optional `MarketingActionType? ActionType` property to `GetMarketingActionsRequest` (in `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/`).

**Acceptance criteria:**
- Property is nullable; absence means "no filter" (matches existing optional-filter convention on the request).
- Property uses the existing `MarketingActionType` enum — no new types introduced.
- The request DTO remains a class (not a record), per project DTO rules.
- Generated TypeScript client exposes the new query parameter after `npm run build`.

### FR-2: Map `ActionType` in the handler
Update `GetMarketingActionsHandler` (lines 24–35) to copy `request.ActionType` onto the `MarketingActionQueryCriteria` it builds, alongside the other existing field mappings.

**Acceptance criteria:**
- Handler maps `ActionType` unconditionally (the nullable value carries the "no filter" signal).
- When `request.ActionType` is `null`, the repository returns the same results as today (regression-safe).
- When `request.ActionType` has a value, the repository returns only actions whose `ActionType` equals the supplied value.

### FR-3: Expose the filter as an HTTP query parameter
The MVC controller endpoint that serves `GetMarketingActions` must accept `actionType` as an optional query string parameter and bind it to the request DTO. No new endpoint or route is required.

**Acceptance criteria:**
- `GET /api/marketing/actions?actionType={value}` returns only matching rows.
- Omitting the parameter returns the current unfiltered behaviour.
- Invalid enum values produce a 400 with a clear error message (default ASP.NET Core model-binding behaviour).

### FR-4: Frontend filter control
Add an "Action Type" dropdown to `MarketingActionFilters.tsx`, populated from the `MarketingActionType` enum values, with a "no filter" default option.

**Acceptance criteria:**
- Dropdown is placed **as the first control** in `MarketingActionFilters.tsx`, to the **left** of the existing "Hledat název..." text input.
- Label and options are Czech-only (the surrounding filter bar uses Czech-only placeholders):
  - Label/placeholder: **"Typ akce"**
  - "No filter" option label: **"Všechny typy"**
  - Enum option labels: `Sociální sítě`, `Blog`, `Newsletter`, `PR`, `Událost`, `Meeting`
- Enum-label strings are centralized in a single shared constant (`ACTION_TYPE_LABELS`) exported from `MarketingActionGrid.tsx` and consumed by both the badge column and the new dropdown. If reusing the export would cause a circular import, extract the map into a new `marketingActionTypeLabels.ts` and have both components import it.
- Dropdown styling matches the existing filter inputs: `border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500`.
- Selecting a value triggers a refetch with the `actionType` query parameter.
- Selecting "Všechny typy" (clearing) refetches without the parameter.
- The existing "Zrušit filtry" reset button also clears the action-type selection. Extend `EMPTY_FILTERS` to include the new field and update `hasActiveFilters` to recognize it.
- Filter state remains **component state only** — do **not** introduce `useSearchParams`/URL state for this filter. The existing four filters do not use URL state; broadening that pattern is out of scope.
- Empty result state renders the existing "no results" UI — no new empty-state design.

### FR-5: Preserve repository behaviour
No change is needed in `MarketingActionRepository.GetPagedAsync`. The existing `if (criteria.ActionType.HasValue)` branch is the activation point.

**Acceptance criteria:**
- No edit to `MarketingActionRepository.cs` for the filter logic itself.
- Existing repository unit/integration tests continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
- The added `WHERE ActionType = @p` predicate must not measurably degrade query latency for the existing paged-list endpoint (target: <10ms additional overhead on a representative dataset).
- A non-clustered index `IX_MarketingActions_ActionType` on `MarketingAction.ActionType` **already exists** (migration `20260424095051_AddMarketingCalendar.cs:99–102`). No new migration is required.

### NFR-2: Security & Authorization
- No new authorization rules. The filter is read-only and inherits the existing authorization on `GetMarketingActions`.
- Input is bound through the typed `MarketingActionType` enum, so SQL injection is not a concern.

### NFR-3: Backwards compatibility
- Existing API clients that do not send `actionType` must observe identical behaviour to today.
- No database migration required.

### NFR-4: Testing
- BE: unit test on `GetMarketingActionsHandler` verifying `ActionType` is propagated to the criteria when set and remains `null` when omitted. Integration test (or in-memory/test-host equivalent) covering the controller binding for `?actionType=` happy path and an invalid-value 400.
- FE: component test on `MarketingActionFilters.tsx` confirming the dropdown renders all enum options with Czech labels, that selection triggers the refetch callback with `actionType` set, that selecting "Všechny typy" clears it, and that "Zrušit filtry" resets the field.
- Aim to meet the project-wide 80% coverage bar on the touched files.

## Data Model
No schema change. The `MarketingAction` entity already carries an `ActionType` column of type `MarketingActionType` (enum), indexed by `IX_MarketingActions_ActionType`. The filter operates on this existing column.

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

**Handler mapping (additive change in `GetMarketingActionsHandler`):**
```csharp
var criteria = new MarketingActionQueryCriteria
{
    // ...existing mappings...
    ActionType = request.ActionType,
};
```

**Frontend filter bar layout (`MarketingActionFilters.tsx`):**
```
[ Typ akce ▾ ] [ Hledat název... ] [ existing filter 3 ] [ existing filter 4 ] [ Zrušit filtry ]
```

**Shared label map (`ACTION_TYPE_LABELS`):**
```ts
// Exported from MarketingActionGrid.tsx (or a new marketingActionTypeLabels.ts if circular-import risk)
export const ACTION_TYPE_LABELS: Record<MarketingActionType, string> = {
  SocialMedia: 'Sociální sítě',
  Blog: 'Blog',
  Newsletter: 'Newsletter',
  PR: 'PR',
  Event: 'Událost',
  Meeting: 'Meeting',
};
```

## Dependencies
- `MarketingActionType` enum (already defined in the Marketing domain).
- OpenAPI client generation pipeline (auto-runs on build).
- Existing list-page filter framework on the frontend (`MarketingActionFilters.tsx`, `EMPTY_FILTERS`, `hasActiveFilters`, refetch hook).
- Existing label rendering in `MarketingActionGrid.tsx:20–36` (the source for the shared `ACTION_TYPE_LABELS` map).

## Out of Scope
- Multi-select filtering (the filter accepts one value at a time). If multi-select is later needed, the request property becomes `IReadOnlyCollection<MarketingActionType>` and the repository switches to a `Contains` predicate.
- Migrating any of the existing four filters in `MarketingActionFilters.tsx` to URL state.
- Other dead-code findings in the Marketing module; this spec addresses only the `ActionType` filter.
- Changing the existing pagination, sorting, or other filter behaviours.
- Reporting, analytics, or saved-filter presets.
- Adding English labels or localization infrastructure to the filter bar (the surrounding bar is Czech-only by convention).

## Open Questions
None.

## Status: COMPLETE