# Design: Type-safe API response handling in MarketingCalendarPage

## Component Design

No new components. `MarketingCalendarPage.tsx` remains the sole mapping boundary (anti-corruption layer) between the generated API DTOs and the two local presentational shapes it feeds. Only the type annotations at that boundary change; runtime structure and responsibilities are unchanged.

```
useMarketingCalendar()  -> GetMarketingCalendarResponse { actions?: ApiMarketingActionCalendarDto[] }
useMarketingActions()   -> GetMarketingActionsResponse  { actions?: ApiMarketingActionDto[], totalPages?: number }
useMarketingAction()    -> GetMarketingActionResponse   { action?: ApiMarketingActionDto }
                                    |
                                    v  (typed mapping, inline in MarketingCalendarPage.tsx)
                         CalendarEvent            (fullcalendarAdapters.ts)      -> MarketingMonthCalendar
                         MarketingActionDto (local, MarketingActionGrid.tsx)     -> MarketingActionGrid / edit modal
```

**Imports added to `MarketingCalendarPage.tsx`** (from `../../../api/generated/api-client`), aliased to avoid colliding with the existing local `MarketingActionDto` import from `../list/MarketingActionGrid`:

```ts
import type {
  GetMarketingCalendarResponse,
  GetMarketingActionsResponse,
  GetMarketingActionResponse,
  MarketingActionDto as ApiMarketingActionDto,
  MarketingActionCalendarDto as ApiMarketingActionCalendarDto,
} from '../../../api/generated/api-client';
```

The existing `import type { MarketingActionDto } from '../list/MarketingActionGrid';` is untouched and keeps meaning the local/grid shape everywhere else in the file (`editingAction` state, `listActions`, the edit modal).

**Responsibility of each mapping site** (unchanged behavior, now type-checked instead of `any`-suppressed):

| Site | Input type | Output type | Callback param annotation |
|---|---|---|---|
| Calendar `.map()` (~line 108-113) | `calendarQuery.data?.actions` → `ApiMarketingActionCalendarDto[] \| undefined` | `CalendarEvent[]` | `a: ApiMarketingActionCalendarDto` |
| List `.map()` (~line 122-128) | `listQuery.data?.actions` → `ApiMarketingActionDto[] \| undefined` | local `MarketingActionDto[]` | `a: ApiMarketingActionDto` |
| Pagination read (~line 136) | `listQuery.data?.totalPages` → `number \| undefined` | `number` (defaults to `1`) | n/a |
| Detail `useEffect` (~line 192-200) | `detailQuery.data?.action` → `ApiMarketingActionDto \| undefined` | local `MarketingActionDto` (`editingAction` state) | `a: ApiMarketingActionDto` (destructured, not a callback) |

No hook, no other component, and no backend contract changes — `useMarketingCalendar`, `useMarketingActions`, `useMarketingAction` already return correctly-typed React Query results once the `as any` casts are deleted (`data` needs no re-declaration; only the `.map()` callback parameters need explicit annotations).

## Data Schemas

Three pre-existing type families meet at this mapping boundary; none are newly introduced, only correctly named/typed in the touched file.

**Generated API DTOs** (`frontend/src/api/generated/api-client.ts`, from NSwag — no regeneration needed):

```ts
// MarketingActionDto (generated) — aliased as ApiMarketingActionDto
{
  id?: number;
  title?: string;
  description?: string;
  actionType?: ...;              // enum, unchanged
  startDate?: Date;
  endDate?: Date | undefined;
  associatedProducts?: string[];
  folderLinks?: ...;
  outlookSyncStatus?: ...;
}

// MarketingActionCalendarDto (generated) — aliased as ApiMarketingActionCalendarDto
{
  id?: number;
  title?: string;
  actionType?: ...;
  startDate?: Date;
  endDate?: Date | undefined;
  associatedProducts?: string[];
  outlookSyncStatus?: ...;
}

// Response envelopes
GetMarketingCalendarResponse { actions?: ApiMarketingActionCalendarDto[] }
GetMarketingActionsResponse  { actions?: ApiMarketingActionDto[]; totalPages?: number }
GetMarketingActionResponse   { action?: ApiMarketingActionDto }
```

Neither generated DTO has, or ever had, a `dateFrom`/`dateTo` field — only `startDate?: Date` / `endDate?: Date | undefined`.

**Local presentational shapes** (unchanged, reused as-is):

```ts
// MarketingActionDto (local, MarketingActionGrid.tsx)
{
  id, title, detail, actionType, associatedProducts, folderLinks, outlookSyncStatus,
  dateFrom?: string | Date;
  dateTo?: string | Date;
}

// CalendarEvent (fullcalendarAdapters.ts)
{
  id, title, actionType, associatedProducts, outlookSyncStatus,
  dateFrom: string;   // YYYY-MM-DD
  dateTo: string;     // YYYY-MM-DD, inclusive
}
```

**Mapping rules at the boundary (replacing the `as any` + dead fallback):**

- Calendar mapping: `dateFrom: a.startDate ? formatDateStr(a.startDate) : ''`, `dateTo: a.endDate ? formatDateStr(a.endDate) : ''` — no `?? a.dateFrom` / `?? a.dateTo`, since that field does not exist on `ApiMarketingActionCalendarDto`.
- List mapping / detail effect: `dateFrom: a.startDate`, `dateTo: a.endDate` — assigned directly, since the local type already accepts `Date`.
- `totalPages: listQuery.data?.totalPages ?? 1` — same runtime expression, now type-checked (`number | undefined` narrowed to `number`).

No database schema changes, no new API endpoints, no event payloads — this is a client-side type-annotation fix at an existing mapping boundary.
