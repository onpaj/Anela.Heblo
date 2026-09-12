# Architecture Review: Type-safe API response handling in MarketingCalendarPage

## Skip Design: true

## Architectural Fit Assessment
This is a pure type-safety cleanup confined to one component's data-mapping boundary — it introduces no new UI, no new endpoints, and no new module. It aligns directly with an existing, explicit project convention: `docs/development/api-client-generation.md` states the NSwag-generated client exists specifically to catch API contract drift at compile time, and explicitly forbids reaching past its typed surface (the doc's own example is `(apiClient as any).http.fetch` for the same reason). `MarketingCalendarPage.tsx` violates that convention four times by casting `useMarketingCalendar`/`useMarketingActions`/`useMarketingAction` query results to `any`.

I verified the spec's central claim directly against `frontend/src/api/generated/api-client.ts`: `MarketingActionDto` (line 32166) and `MarketingActionCalendarDto` (line 32401) both expose only `startDate?: Date` / `endDate?: Date | undefined` — there is no `dateFrom`/`dateTo` field anywhere in the generated client, and none in the backend DTOs it mirrors. The `?? a.dateFrom` / `?? a.dateTo` fallbacks in the page (lines 112–113, 127–128, 199–200) are therefore dead code that only compiles today because `as any` suppresses the error. The spec's conclusion is correct, not merely plausible — I confirmed it by reading the generated class bodies, not by trusting the spec's prose.

I also confirmed the naming collision the spec flags: `MarketingActionGrid.tsx` (line 8) defines its own local `export interface MarketingActionDto` with a genuinely different shape (`dateFrom?: string | Date` / `dateTo?: string | Date`, no `createdAt`/`modifiedAt`/audit fields), and `MarketingCalendarPage.tsx` already imports that local type (line 7) for its own state (`editingAction`, `listActions`). Importing the generated `MarketingActionDto` unaliased into the same file would collide with that existing import — this is almost certainly why `as any` was reached for originally, and the spec's proposed alias (`ApiMarketingActionDto`) is the correct, minimal fix.

One thing the spec doesn't mention but I found while exploring: `frontend/src/components/marketing/calendar/MobileAgendaView.tsx` (lines 62–63, 69–70, 79, 83–84) has the **identical** `as any` + dead `?? a.dateFrom` pattern, reading from the same two hooks. The spec explicitly scopes this out ("Auditing or fixing `as any` casts in other marketing components... beyond the four call sites named in the brief"), which is a reasonable scope boundary for this ticket, but it means the same defect survives untouched in a second file. See Specification Amendments.

## Proposed Architecture

### Component Overview
No new components. The existing mapping boundary is being made type-safe in place:

```
useMarketingCalendar()  --> GetMarketingCalendarResponse { actions: ApiMarketingActionCalendarDto[] }
useMarketingActions()   --> GetMarketingActionsResponse  { actions: ApiMarketingActionDto[], totalPages }
useMarketingAction()    --> GetMarketingActionResponse   { action: ApiMarketingActionDto }
                                    |
                                    v  (mapping in MarketingCalendarPage.tsx — unchanged responsibility)
                         CalendarEvent (fullcalendarAdapters.ts)   -- for MarketingMonthCalendar
                         MarketingActionDto (local, MarketingActionGrid.tsx) -- for MarketingActionGrid / MarketingActionModal
```

`MarketingCalendarPage.tsx` remains the sole anti-corruption layer between the generated API DTOs and the two local presentational shapes. Nothing about this data flow changes — only the types used to describe it stop lying.

### Key Design Decisions

#### Decision 1: Alias the generated DTOs on import rather than renaming either type
**Options considered:**
1. Rename the local `MarketingActionDto` in `MarketingActionGrid.tsx` to something like `MarketingActionRow`.
2. Import the generated types under aliases (`ApiMarketingActionDto`, `ApiMarketingActionCalendarDto`) and leave both `MarketingActionDto` names as-is.
3. Keep `as any` and add a runtime shape-check instead (rejected outright — doesn't address the stated goal).

**Chosen approach:** Option 2, exactly as the spec proposes.

**Rationale:** The local `MarketingActionDto` is a public export consumed by `MarketingActionGrid.tsx`, `MarketingActionModal.tsx`, `MobileAgendaView.tsx`, and this page — renaming it is a wider blast radius than this ticket's stated scope (spec's NFR/Out-of-Scope already excludes it) and would touch files that aren't misbehaving. Aliasing the *newly introduced* import in the one file that needs both is strictly smaller, is a standard TypeScript idiom (`import { Foo as Bar }`), and requires no change outside `MarketingCalendarPage.tsx`.

#### Decision 2: Do not centralize the DTO→presentational mapping into a shared function as part of this change
**Options considered:**
1. Extract `mapToCalendarEvent(a: ApiMarketingActionCalendarDto): CalendarEvent` and `mapToGridDto(a: ApiMarketingActionDto): MarketingActionDto` as named functions (in this file or a new `marketingMappers.ts`), used by both `MarketingCalendarPage.tsx` and `MobileAgendaView.tsx`.
2. Leave the mapping inline in each `useMemo`/`useEffect`, fixed independently in each file, as the spec scopes it.

**Chosen approach:** Option 2 for this ticket; flagged as a follow-up (see Specification Amendments) rather than pulled in now.

**Rationale:** `development_guidelines.md`'s "surgical changes" doctrine and this spec's own Out-of-Scope section both point the same direction: touch only the four call sites named in the brief. Extracting a shared mapper is a legitimate improvement (it would prevent `MobileAgendaView.tsx` from silently reintroducing the same `any`-cast bug later) but it changes two files instead of one and isn't required to satisfy the brief. Doing it now would be scope creep relative to what was asked; doing it as a fast, separate follow-up captures the value without inflating this PR's risk surface.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. All changes confined to:
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`

No other file needs to change (hooks, generated client, `MarketingActionGrid.tsx`, `fullcalendarAdapters.ts` are all correct as-is and are only *consumed*, per the spec's Dependencies section — confirmed by reading each).

### Interfaces and Contracts
Import additions in `MarketingCalendarPage.tsx` (all from `../../../api/generated/api-client`, verified to exist at the lines shown):
```ts
import type {
  GetMarketingCalendarResponse,
  GetMarketingActionsResponse,
  GetMarketingActionResponse,
  MarketingActionDto as ApiMarketingActionDto,
  MarketingActionCalendarDto as ApiMarketingActionCalendarDto,
} from '../../../api/generated/api-client';
```
The existing `import type { MarketingActionDto } from '../list/MarketingActionGrid';` (line 7) stays untouched — it continues to mean the local/grid shape everywhere else in the file (`editingAction`, `listActions`, the `useState<MarketingActionDto | null>`).

Because `useMarketingCalendar`/`useMarketingActions`/`useMarketingAction` already return the fully-typed React Query result (confirmed by reading `useMarketingCalendar.ts` — each `queryFn` returns the awaited generated-client call directly, no wrapping or stripping of types), `calendarQuery.data`, `listQuery.data`, and `detailQuery.data` are already statically `GetMarketingCalendarResponse | undefined` etc. **without any cast** — the explicit type imports above are for annotating the `.map()` callback parameters (`a: ApiMarketingActionCalendarDto`, `a: ApiMarketingActionDto`), not for re-declaring the query `data` fields, since TypeScript already infers those correctly once the `as any` is deleted.

### Data Flow
Unchanged from today, made explicit:
1. `calendarQuery.data?.actions` (typed `ApiMarketingActionCalendarDto[] | undefined`) → `.map(a => ({ ...CalendarEvent shape... }))`, with `dateFrom`/`dateTo` computed as `a.startDate ? formatDateStr(a.startDate) : ''` (drop the `instanceof Date` else-branch fallback to `a.dateFrom`, since `a.startDate` is now statically known to be `Date | undefined`, never a pre-formatted string — the `instanceof Date` guard itself becomes redundant but the spec doesn't ask to remove it and I see no reason to volunteer that separately; leaving the guard as dead-but-harmless is acceptable here rather than risking a behavior change no one asked to verify).
2. `listQuery.data?.actions` (typed `ApiMarketingActionDto[] | undefined`) → `.map(a => ({ ...local MarketingActionDto shape... }))`, with `dateFrom: a.startDate, dateTo: a.endDate` directly (both types accept `Date`, per `MarketingActionGrid.tsx` line 13-14: `dateFrom?: string | Date`).
3. `listQuery.data?.totalPages ?? 1` — same expression, now type-checked instead of blindly trusted.
4. `detailQuery.data?.action` (typed `ApiMarketingActionDto | undefined`) → same field-by-field assignment into `editingAction`, dropping `?? a.dateFrom`/`?? a.dateTo`.

No change to `MarketingActionGrid`, `fullcalendarAdapters`, `MarketingActionModal`, or any backend code, and no NSwag regeneration — confirmed unnecessary since the generated client already carries the correct types today.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Removing the `instanceof Date` fallback branch in the calendar mapping changes behavior if `startDate` is ever *not* a `Date` at runtime (e.g., a stale cached response from before a hook change) | Low | Fallback to `''` on absence only, as the spec specifies (FR-5) — this matches current behavior for all real API responses; `formatDateStr` is only ever called on a genuine `Date` per the generated client's `init()`, which always constructs `new Date(...)` from the wire payload |
| Import alias adds a second name for the same underlying data shape (`MarketingActionDto` vs `ApiMarketingActionDto`) in one file, which is intrinsically more confusing than a single name | Low | Confined to this one file, which is already documented (by the spec's Data Model table) as the deliberate API↔presentational mapping boundary; a repo-wide rename is explicitly out of scope and would be a larger, separate change |
| `MobileAgendaView.tsx` retains the exact same `as any` + dead-fallback defect this ticket fixes elsewhere, so the underlying anti-pattern isn't eradicated from the module | Medium | Explicitly out of scope per spec; recommend filing a fast follow-up ticket (see Specification Amendments) rather than silently letting arch-review re-discover it later |
| `npm run build` / `tsc` surfaces additional latent type errors once `any` is removed (e.g. `a.id!` non-null assertion on an `id?: number`, or optional `title`/`actionType` fields feeding non-optional local fields) | Low | These are exactly the compile errors NFR-2 wants; fix each by keeping the existing `?? ''` / `?? 'Other'` defaulting already present in the code — no field in the acceptance criteria requires removing those |

## Specification Amendments
- **No functional amendment needed** — the spec's technical analysis (generated types, collision, dead fallbacks) was independently verified against the actual generated client and matches exactly.
- **Clarify FR-1's phrasing**: "The `.map()` callback parameter must be typed as the generated `MarketingActionCalendarDto`" should be read as *the callback parameter annotation*, not a re-declaration of `calendarQuery.data`'s type — `data` is already correctly typed once the cast is removed, per `useMarketingCalendar.ts`'s `queryFn`. No behavior or acceptance-criteria change, just precision for whoever implements it.
- **Recommend (non-blocking) a follow-up ticket** for `MobileAgendaView.tsx` (lines 62–63, 69–70, 79, 83–84), which has the identical `as any`/dead-fallback pattern reading from the same two hooks (`useMarketingCalendar`, `useMarketingAction`). Leaving it out of *this* PR is correct per the spec's Out-of-Scope section and keeps this change surgical, but the duplication means the same class of bug (compile-time blindness to a backend field rename) still exists in the module after this ships.

## Prerequisites
None. No migrations, no config, no infrastructure changes, no NSwag regeneration. The generated client already contains every type this change needs (confirmed by direct inspection of `frontend/src/api/generated/api-client.ts`).
