# Design: Fix Issued Invoices page shell blocked by grid-data loading (Seznam tab never appears)

## Component Design

No new components. Two existing modules change; both keep their current external call sites
except for the one new argument described below.

### `useIssuedInvoicesList` (`frontend/src/api/hooks/useIssuedInvoices.ts`)

**Current signature:**
```ts
export const useIssuedInvoicesList = (filters: IssuedInvoicesFilters) => { ... }
```

**New signature:**
```ts
export interface UseIssuedInvoicesListOptions {
  enabled?: boolean;
}

export const useIssuedInvoicesList = (
  filters: IssuedInvoicesFilters,
  options?: UseIssuedInvoicesListOptions,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.issuedInvoices, filters],
    queryFn: async (): Promise<IGetIssuedInvoicesListResponse> => { /* unchanged */ },
    enabled: options?.enabled ?? true,
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
  });
};
```

Contract notes (per arch review Decision 2 and Interfaces/Contracts section):
- `options` is a **second, optional** parameter, separate from `filters`. `enabled` is a query-gating
  concern, not a filter value, and must never be merged into `IssuedInvoicesFilters` or appended to
  `queryKey` — doing so would change the cache key and break the `staleTime`-based "no refetch on
  tab-switch-back" acceptance criterion (FR-2).
- Default (`options` omitted, or `options.enabled` omitted) is `true`, so any other caller of this
  hook (if one is ever added outside `IssuedInvoicesPage`) keeps today's eager-fetch behavior with
  zero changes required at that call site.
- Mirrors the existing internal pattern in the same file: `useIssuedInvoiceDetail` already passes
  `enabled: !!invoiceId` into `useQuery`; this hook now exposes the same React Query primitive to
  its caller instead of hardcoding `true`.
- No change to `queryFn`, `queryKey` shape (still `[...QUERY_KEYS.issuedInvoices, filters]`),
  `staleTime`, or `gcTime`. No change to the shape of data returned or thrown.

**Call site — `IssuedInvoicesPage.tsx`:**
```ts
const { data, isLoading: loading, error } = useIssuedInvoicesList(
  { pageNumber, pageSize, sortBy, sortDescending, /* ...existing filters */ },
  { enabled: activeTab === 'grid' },
);
```

### `IssuedInvoicesPage` (`frontend/src/pages/customer/IssuedInvoicesPage.tsx`)

Responsibility split changes from "page owns list-loading state and gates all rendering on it" to
"page owns tab state only; each tab owns its own data lifecycle" — matching the already-correct
sibling pattern in `BankStatementsOverviewPage.tsx` and the already-correct `StatisticsTab` in this
same file.

- **Remove**: the page-level `if (loading) return ...` block (lines 314–323) and `if (error)
  return ...` block (lines 325–334). These currently execute before the `<h1>`/tab-`<nav>` JSX and
  are the direct cause of the bug; the arch review's Decision 1 selects deleting them outright
  (Option 1) over narrowing the condition (Option 2, rejected as reintroducing the same coupling) or
  extracting a `GridTab` component (Option 3, out of scope per "surgical changes").
- **Unchanged**: the `<h1>Vydané faktury</h1>` heading and the tab `<nav>` (Statistiky / Seznam
  buttons) — these already render correctly once nothing short-circuits ahead of them; no JSX
  changes needed there.
- **Unchanged**: `StatisticsTab` inline component (lines 337–358) — already scopes
  `syncStatsLoading`/`syncStatsError` to itself; not touched by this fix.
- **Now reachable (previously dead code)**: the grid-scoped loading/error block at lines 591–604,
  inside the `activeTab === 'grid'` branch. This becomes the sole loading/error UI for the list
  query. Add `data-loading="true"` to its loading container (currently a plain `<div>` at line
  592) so `waitForLoadingComplete()` (`frontend/test/e2e/helpers/wait-helpers.ts`, matches
  `[data-loading="true"], .loading, .spinner, [aria-busy="true"]`) actually blocks instead of
  no-op'ing, per FR-4 / Decision 3.
- **Unchanged**: the existing `useEffect` at lines 191–195 that only fires `refetchRunningJobs` when
  `activeTab === 'grid'` — this is the precedent the new `enabled` gate follows, not something this
  fix modifies.
- **Unchanged**: `useIssuedInvoiceSyncStats` call — remains unconditional/eager on mount, since
  Statistics is the default tab (spec Open Question 4 explicitly keeps this out of scope).

### Data flow after the fix

1. Mount, `activeTab = 'statistics'`. Shell (`<h1>` + tab `<nav>`) renders immediately with no data
   dependency.
2. `useIssuedInvoiceSyncStats` fires unconditionally; `StatisticsTab` shows its own scoped
   loading/error/content.
3. `useIssuedInvoicesList` does not fire (`enabled: false` while `activeTab !== 'grid'`) — no
   `/api/invoices` request issued.
4. User clicks "Seznam" → `activeTab` becomes `'grid'` → `enabled` becomes `true` →
   `useIssuedInvoicesList` fires `GET /api/invoices` for the first time → the grid panel (not the
   page) shows its scoped spinner (now carrying `data-loading="true"`) → resolves to data or a
   scoped error, while the header and tab bar stay interactive throughout.
5. Switching tabs back and forth reuses React Query's cache (`staleTime: 5 min`, `gcTime: 10 min`)
   unchanged; toggling `enabled` does not evict cached data or reset `queryKey`.

## Data Schemas

No changes.

- No database schema changes.
- No API request/response shape changes: `GET /api/invoices` and `GetIssuedInvoicesListResponse`
  (`IssuedInvoiceDto`, `Items`, `TotalCount`, `PageNumber`, `PageSize`, `Success`, `ErrorCode`,
  `Params`) are unmodified — this is a frontend rendering/query-gating fix only, and Decision 4
  explicitly defers both the FR-5 latency diagnosis and the `Success: false`-swallowed-as-empty-list
  correctness fix to a separate follow-up PR.
- No event payload changes.
- Frontend-internal type addition only: `UseIssuedInvoicesListOptions { enabled?: boolean }` in
  `useIssuedInvoices.ts`, consumed solely by `useIssuedInvoicesList`'s own signature — not part of
  any wire contract, not exported to `IssuedInvoicesFilters`, not part of the React Query cache key.
