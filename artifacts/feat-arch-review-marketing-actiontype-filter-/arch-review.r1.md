Exploration confirms what the spec promises and surfaces a few inconsistencies the implementer needs to know. Writing the review now.

# Architecture Review: Marketing Action Type Filter

## Skip Design: true
No new visual primitives — the dropdown reuses the exact styling of the existing `searchText`/date inputs in `MarketingActionFilters.tsx`. No layout, color, or spacing decisions to be made.

## Architectural Fit Assessment
The feature is a clean **additive** change inside the existing Marketing vertical slice. Every architectural boundary it touches is already established:

- **Domain → Application → Persistence wiring**: `MarketingActionQueryCriteria.ActionType` (Domain), the `if (criteria.ActionType.HasValue)` branch in `MarketingActionRepository.GetPagedAsync` (Persistence:62–65), and the EF index `IX_MarketingActions_ActionType` (migration `20260424095051`:98–102) already exist. Only the **Application contract** (request DTO) and the **handler mapping** are missing.
- **HTTP transport**: The endpoint is served by `MarketingCalendarController.GetMarketingActions` at `GET /api/MarketingCalendar`, which uses `[FromQuery] GetMarketingActionsRequest`. Adding a property to the DTO is sufficient — no controller code changes.
- **Frontend integration**: `MarketingCalendarPage.tsx` (list view, lines 89–94) already maps a `MarketingFilters` state object onto `useMarketingActions` params. The hook at `frontend/src/api/hooks/useMarketingCalendar.ts:42–62` is a thin positional wrapper around the generated client method. The TS `MarketingActionType` enum is already exported by the generated client (`api-client.ts:25651`).

There are **two real architectural issues** the spec underspecifies, both surfaced during exploration:

1. **`MarketingActionGrid.tsx:20–36` `ACTION_TYPE_LABELS` and `ACTION_TYPE_BADGE` maps are keyed against names that do NOT exist in the API enum** (`General`, `Promotion`, `Launch`, `Campaign`, `Event`, `Other`). The labels are also mis-assigned (`Promotion → "Událost"`, `Launch → "Email"`). The canonical, correct mapping already exists in `fullcalendarAdapters.ts:13–20` (`ACTION_TYPE_COLORS`) and uses the real enum keys (`SocialMedia`, `Blog`, `Newsletter`, `PR`, `Event`, `Meeting`). The spec's instruction to "reuse the exported `ACTION_TYPE_LABELS` from `MarketingActionGrid.tsx`" would propagate the existing bug. **The label map must be rebuilt against the real enum values, not exported as-is.**
2. **The spec's "existing four filters" wording is wrong** — `MarketingActionFilters.tsx:4–8` defines exactly three: `searchText`, `dateFrom`, `dateTo`. The `ActionType` dropdown becomes the **fourth** filter.

## Proposed Architecture

### Component Overview
```
┌───────────────────────────────────────────────────────────────────────┐
│                          Frontend (React)                              │
│                                                                        │
│  MarketingCalendarPage.tsx                                             │
│    ├─ filters: MarketingFilters { searchText, dateFrom, dateTo,        │
│    │                              actionType }      ◄── NEW field      │
│    └─ useMarketingActions({ ..., actionType })  ◄── NEW param          │
│           │                                                            │
│  MarketingActionFilters.tsx  ◄── add dropdown (1st position)           │
│           │                                                            │
│  marketingActionTypeLabels.ts  ◄── NEW shared module                   │
│    └─ ACTION_TYPE_LABELS, ACTION_TYPE_BADGE keyed by real enum         │
│           │                                                            │
│  useMarketingCalendar.ts → generated api-client.ts                     │
│    └─ positional arg list extended with actionType                     │
│           ▼                                                            │
└──────────── GET /api/MarketingCalendar?ActionType=Blog ───────────────┘
             ▼
┌───────────────────────────────────────────────────────────────────────┐
│                          Backend (.NET 8)                              │
│                                                                        │
│  MarketingCalendarController.GetMarketingActions  (unchanged)          │
│           │ [FromQuery] GetMarketingActionsRequest                     │
│           ▼                                                            │
│  GetMarketingActionsRequest  ◄── add MarketingActionType? ActionType   │
│           │                                                            │
│  GetMarketingActionsHandler  ◄── map ActionType into criteria          │
│           │                                                            │
│  MarketingActionRepository.GetPagedAsync  (already filters; unchanged) │
│           │                                                            │
│           ▼  WHERE ActionType = @p  (index: IX_MarketingActions_ActionType)
└───────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the shared `ACTION_TYPE_LABELS` map lives
**Options considered:**
- A. Export from `MarketingActionGrid.tsx` and import into `MarketingActionFilters.tsx` (spec's preferred option).
- B. Extract into a new `frontend/src/components/marketing/list/marketingActionTypeLabels.ts` module that both files import.

**Chosen approach:** **B (new module)**.

**Rationale:** Both files already pull from the same source (`MarketingActionFilters.tsx` and `MarketingActionGrid.tsx` are siblings, no cycle today), so option A is technically fine. But the spec acknowledges that a circular-import risk is plausible if either file grows. More importantly, `MarketingActionGrid.tsx` currently **does not import any enum-keyed map** — its existing maps are string-keyed against bogus keys. Forcing it to be the owner of the canonical map mixes concerns (presentation grid + enum dictionary). A small dedicated module keeps the canonical mapping testable, free of UI dependencies, and naturally co-locates with `fullcalendarAdapters.ts`'s color map.

#### Decision 2: Should the filter use the existing dead infrastructure, or rebuild it?
**Options considered:**
- A. Wire up the existing dead code path (spec's choice).
- B. Remove the dead code and add the filter fresh.

**Chosen approach:** **A — wire up the existing path.**

**Rationale:** The DB index, criteria field, and repository branch are already in place, tested by the migration history, and semantically correct. Removing and re-adding would create a no-op migration churn and lose review evidence that the original author intended the filter. The brief, the spec, and the existing index all point the same direction.

#### Decision 3: Request DTO type for `ActionType` parameter
**Options considered:**
- A. `MarketingActionType?` (typed enum, nullable).
- B. `string?` parsed in the handler.
- C. `int?` for the underlying enum value.

**Chosen approach:** **A — typed `MarketingActionType?`.**

**Rationale:** ASP.NET Core's default model binder accepts both string (`?ActionType=Blog`) and numeric (`?ActionType=1`) values for nullable enum query parameters and produces a 400 with a clear error for invalid values. Typing the property as the enum gives OpenAPI a schema-validated `enum` parameter, the NSwag generator produces a typed TS parameter, and the handler keeps a one-line mapping. Matches the request-DTO conventions for `Create`/`Update` in the same module (which take `MarketingActionType` directly).

#### Decision 4: Where the filter state lives on the frontend
**Options considered:**
- A. Component state (current pattern for all three existing filters).
- B. URL query params via `useSearchParams`.

**Chosen approach:** **A — component state only.**

**Rationale:** Matches the established convention in `MarketingCalendarPage.tsx:56`. The spec explicitly scopes URL-state out. Don't broaden a pattern that wasn't asked for.

## Implementation Guidance

### Directory / Module Structure
**New files**
- `frontend/src/components/marketing/list/marketingActionTypeLabels.ts` — shared dictionary module (see contract below).

**Modified files**
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/GetMarketingActionsRequest.cs` — add `MarketingActionType? ActionType` property and `using Anela.Heblo.Domain.Features.Marketing;`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs:24–35` — add `ActionType = request.ActionType,` to the criteria initializer.
- `frontend/src/components/marketing/list/MarketingActionFilters.tsx` — extend `Filters` interface with `actionType: MarketingActionType | ""`; extend `EMPTY_FILTERS`; extend `hasActiveFilters`; render the dropdown as the first control; export the existing `MarketingFilters` type alias unchanged.
- `frontend/src/components/marketing/list/MarketingActionGrid.tsx` — rebuild `ACTION_TYPE_LABELS`/`ACTION_TYPE_BADGE` against the real enum **and** import them from the new shared module.
- `frontend/src/components/marketing/list/__tests__/MarketingActionGrid.test.tsx` — fixtures currently use `actionType: "Campaign" | "Launch" | "General"` (lines 18, 27, 99, 237, 254, 268) which are **not real enum values**. These tests pass today only because the bogus keys hit a fallback. After the canonical map is fixed, the tests must be updated to use real enum values (e.g., `SocialMedia`, `Blog`, …) or they will break. Treat this as an in-scope fix because the filter feature requires correcting the label map.
- `frontend/src/api/hooks/useMarketingCalendar.ts` — add `actionType?: MarketingActionType` to `GetMarketingActionsParams`; pass `params.actionType` as the **next argument** in the positional call to `marketingCalendar_GetMarketingActions` (argument order must match regenerated client).
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx:89–94` — extend the `useMarketingActions` arg with `actionType: filters.actionType || undefined`.
- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx:64–69` — the `EMPTY_FILTERS` mock literal must include the new `actionType: ""` field, otherwise the test setup drifts from production.

### Interfaces and Contracts

**Backend — `GetMarketingActionsRequest` (final shape):**
```csharp
public class GetMarketingActionsRequest : IRequest<GetMarketingActionsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SearchTerm { get; set; }
    public MarketingActionType? ActionType { get; set; }   // NEW
    public string? ProductCodePrefix { get; set; }
    public DateTime? StartDateFrom { get; set; }
    public DateTime? StartDateTo { get; set; }
    public DateTime? EndDateFrom { get; set; }
    public DateTime? EndDateTo { get; set; }
    public bool IncludeDeleted { get; set; } = false;
}
```
Class, not record (project rule for OpenAPI-generated DTOs).

**Frontend — `marketingActionTypeLabels.ts` (new module):**
```ts
import { MarketingActionType } from '../../../api/generated/api-client';

export const ACTION_TYPE_LABELS: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'Sociální sítě',
  [MarketingActionType.Blog]:        'Blog',
  [MarketingActionType.Newsletter]:  'Newsletter',
  [MarketingActionType.PR]:          'PR',
  [MarketingActionType.Event]:       'Událost',
  [MarketingActionType.Meeting]:     'Meeting',
};

export const ACTION_TYPE_BADGE: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'bg-yellow-100 text-yellow-800',
  [MarketingActionType.Blog]:        'bg-green-100 text-green-800',
  [MarketingActionType.Newsletter]:  'bg-purple-100 text-purple-800',
  [MarketingActionType.PR]:          'bg-orange-100 text-orange-800',
  [MarketingActionType.Event]:       'bg-red-100 text-red-800',
  [MarketingActionType.Meeting]:     'bg-teal-100 text-teal-800',
};

export const ALL_ACTION_TYPE_OPTIONS = [
  MarketingActionType.SocialMedia,
  MarketingActionType.Blog,
  MarketingActionType.Newsletter,
  MarketingActionType.PR,
  MarketingActionType.Event,
  MarketingActionType.Meeting,
] as const;
```

**Frontend — `Filters` (final shape in `MarketingActionFilters.tsx`):**
```ts
interface Filters {
  searchText: string;
  dateFrom: string;
  dateTo: string;
  actionType: MarketingActionType | '';   // '' = no filter, matches existing string convention
}

const EMPTY_FILTERS: Filters = {
  searchText: '', dateFrom: '', dateTo: '', actionType: '',
};

const hasActiveFilters = (f: Filters) =>
  f.searchText !== '' || f.dateFrom !== '' || f.dateTo !== '' || f.actionType !== '';
```

**HTTP contract (post-regeneration):**
```
GET /api/MarketingCalendar
  ?PageNumber=1
  &PageSize=20
  &SearchTerm=...
  &ActionType=Blog          # NEW — case-insensitive; accepts enum string or int
  &StartDateFrom=...
  ...
```
(Note: the controller route is `/api/MarketingCalendar`, **not** `/api/marketing/actions` as the spec writes — spec amendment below.)

### Data Flow

1. User picks "Blog" in the dropdown → `MarketingActionFilters` calls `onChange({ ..., actionType: 'Blog' })`.
2. `MarketingCalendarPage` updates `filters` state and resets `pageNumber` to 1.
3. `useMarketingActions({ ..., actionType: 'Blog' })` rebuilds its React Query key → new query.
4. Generated client emits `GET /api/MarketingCalendar?ActionType=Blog&...`.
5. ASP.NET model binder parses `"Blog"` → `MarketingActionType.Blog` on the request DTO.
6. `GetMarketingActionsHandler` copies `request.ActionType` into `MarketingActionQueryCriteria.ActionType`.
7. `MarketingActionRepository.GetPagedAsync` adds the `WHERE ActionType = @p` predicate (existing branch at line 62–65), uses `IX_MarketingActions_ActionType`.
8. Response returns paged rows; React Query renders grid with the existing label/badge map (now correct).
9. Selecting "Všechny typy" sets `actionType: ''` → query param dropped → unfiltered result.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec instructs reuse of broken `ACTION_TYPE_LABELS` map → label regression on the grid would be invisible until users complain | **HIGH** | Rebuild the map against the real enum (Decision 1); update `MarketingActionGrid.test.tsx` fixtures to use real enum values. |
| `MarketingActionGrid.test.tsx` fixtures use non-existent keys (`Campaign`, `Launch`, `General`) and assert on their mis-mapped Czech labels (`"PR"`, `"Email"`) | **HIGH** | Update fixtures and assertions to use real enum keys; the test "shows Czech label for Campaign action type" (line 85) and "Launch" (line 90) are testing the bug, not the contract — replace them. |
| Spec quotes wrong endpoint route (`/api/marketing/actions`) | LOW | Real route is `/api/MarketingCalendar` per `MarketingCalendarController:12`. Document correctly in PR. |
| `useMarketingCalendar.ts` hook calls the generated client **positionally**, so the new `actionType` arg must slot into the right position after regeneration | MEDIUM | Diff the regenerated `marketingCalendar_GetMarketingActions` signature after `npm run build`; verify positional order matches the hook's pass-through; add a typed wrapper if positional drift becomes a recurring risk (out of scope for this PR). |
| `MarketingCalendarPage.test.tsx` mocks `EMPTY_FILTERS` as a literal at line 69 — adding `actionType` to the production constant without updating the mock will leave the test using a stale shape | MEDIUM | Update the mock literal in the same PR. |
| Enum query-parameter binding edge case: numeric `0` (`SocialMedia`) sent as `?ActionType=0` must work | LOW | Default ASP.NET Core binder handles both numeric and string forms. Cover with a controller-level integration test (one happy path + one 400). |

## Specification Amendments

1. **FR-4 (Czech labels for `Event` and `Meeting`)**: Spec maps `Event → "Událost"` and `Meeting → "Meeting"`. Confirm these are intended (current broken map has `Promotion → "Událost"` and `Other → "Ostatní"`). Proceed with spec values; flag to product if a different translation is expected.
2. **FR-4 (label-map location)**: Spec says "centralized in a single shared constant exported from `MarketingActionGrid.tsx`". Override: extract into `frontend/src/components/marketing/list/marketingActionTypeLabels.ts` (Decision 1) and update `MarketingActionGrid.tsx` to consume it. The current `MarketingActionGrid.tsx` map is incorrect and must be rebuilt against the real enum, regardless of where it lives.
3. **API/Interface Design**: The endpoint route shown as `GET /api/marketing/actions` is wrong. The actual route is `GET /api/MarketingCalendar`. Update the spec.
4. **FR-4 (filter count)**: Spec references "the existing four filters". The filter bar currently has three (`searchText`, `dateFrom`, `dateTo`). The new dropdown is the fourth. Tighten wording.
5. **Query parameter casing**: After OpenAPI regeneration, the generated TS client emits PascalCase query keys (`ActionType`, not `actionType`). ASP.NET Core model binding is case-insensitive so both work, but the documented contract should match the generator output (`ActionType`).
6. **NFR-4 (test scope)**: Add a test entry — "`MarketingActionGrid.test.tsx` fixtures must be updated to use real `MarketingActionType` enum values; the existing 'Campaign'/'Launch'/'General' assertions are testing the existing label-map bug." Without this, the canonical map fix breaks two passing tests.
7. **Open Questions**: One additional item — confirm `Meeting` should stay untranslated (`"Meeting"`) on the Czech UI. Spec asserts it does; flag for product sign-off.

## Prerequisites

- **None blocking implementation.** The DB index, criteria field, repository branch, controller, MediatR pipeline, and TS enum binding all exist.
- **OpenAPI regeneration runs automatically on `npm run build`**, so adding the `ActionType` property to the C# DTO will produce a regenerated `api-client.ts`. The implementer must run a backend build first, then a frontend build, to materialize the new argument before editing `useMarketingCalendar.ts:42–62`.
- **No migration required** — `IX_MarketingActions_ActionType` already exists in `20260424095051_AddMarketingCalendar.cs:98–102`.
- **No configuration / secret / infrastructure changes.**