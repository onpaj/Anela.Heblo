# Implementation: decouple-issued-invoices-shell-from-grid-loading

## What was implemented

Decoupled the Issued Invoices page shell (header + tab navigation) from the grid-only
`useIssuedInvoicesList` query's loading/error state, and made that query lazy so it only fires
once the user opens the "Seznam" (grid) tab.

1. `useIssuedInvoicesList` now accepts an optional second `options?: { enabled?: boolean }`
   parameter, wired to React Query's `enabled` (default `true` when omitted, preserving existing
   eager-fetch behavior for any other/future caller). `queryKey`, `queryFn` body, `staleTime`, and
   `gcTime` are byte-for-byte unchanged.
2. `IssuedInvoicesPage.tsx` now calls `useIssuedInvoicesList({...filters}, { enabled: activeTab === 'grid' })`.
3. Removed the page-level `if (loading) return ...` / `if (error) return ...` early-return blocks
   that previously ran before the `<h1>` and tab `<nav>` were reached, blocking the whole page
   shell on a query that the default Statistics tab never needed.
4. Added `data-loading="true"` to the grid tab's own scoped loading container (the ternary at the
   `activeTab === 'grid'` branch), which was previously dead code and is now reachable — this
   marker is what `waitForLoadingComplete()` (`frontend/test/e2e/helpers/wait-helpers.ts`) actually
   matches on.

The `StatisticsTab`'s own loading/error handling, the `Success: false`/`if (!response.ok) throw`
logic in `queryFn`, the `useEffect` gating `refetchRunningJobs()` on `activeTab === 'grid'`, and all
backend/OpenAPI-generated files were left untouched, per the task's explicit scope.

## Files created/modified

- `frontend/src/api/hooks/useIssuedInvoices.ts` — added `UseIssuedInvoicesListOptions` interface;
  `useIssuedInvoicesList` takes an optional second `options` argument; added
  `enabled: options?.enabled ?? true` to the `useQuery({...})` config.
- `frontend/src/pages/customer/IssuedInvoicesPage.tsx` — call site now passes
  `{ enabled: activeTab === 'grid' }`; deleted the page-level `if (loading)`/`if (error)`
  early-return blocks; added `data-loading="true"` to the grid tab's loading container.

## Tests

- `cd frontend && npm run build` — passed, "Compiled successfully." (no new errors; only
  pre-existing `caniuse-lite`/bundle-size informational warnings unrelated to this change).
- `cd frontend && npm run lint` — ran across the whole repo; there are 148 pre-existing
  errors/14 warnings in unrelated test files (e.g. `testing-library/no-node-access`,
  `testing-library/no-wait-for-multiple-assertions` in various `__tests__` files across
  `marketing/`, `financial-overview/`, `terminal/`, etc.) that are baseline issues, not introduced
  by this change. Verified via `npm run lint 2>&1 | grep -A5 "IssuedInvoicesPage\|useIssuedInvoices"`
  that neither of the two files touched by this task appears anywhere in the lint output — i.e.
  zero new lint errors/warnings from this diff.
- E2E: **not runnable in this sandbox** — no live backend/browser available. Per the task's
  validation guidance, the 29 specs under `frontend/test/e2e/issued-invoices/` should be confirmed
  on the next nightly staging run (`./scripts/run-playwright-tests.sh`) per CLAUDE.md's E2E
  validation rule (nightly suite, not part of PR CI). This was not run and is not claimed as
  validated here.

Note: `node_modules` was not present in this worktree checkout; it was symlinked from the primary
worktree (`/home/user/Anela.Heblo/frontend/node_modules`) to run build/lint, after confirming both
worktrees share an identical `package-lock.json`. `node_modules` is gitignored, so this has no
effect on the commit.

## How to verify

1. `cd frontend && npm run build && npm run lint` — confirm clean build and no lint errors on the
   two changed files.
2. Manual/dev check: load `/customer/issued-invoices` with the Network tab open — confirm
   `GET /api/invoices` is not issued until "Seznam" is clicked, and that the header (`Vydané
   faktury`) and both tab buttons render immediately regardless of that request's timing.
3. Switch Statistics → Seznam → Statistics → Seznam again within 5 minutes and confirm only one
   `GET /api/invoices` request is logged (React Query `staleTime` cache reuse).
4. Confirm the "Načítání faktur..." spinner and "Chyba při načítání faktur" message only ever
   appear inside the grid tab's content region (with `data-loading="true"` present on the spinner
   container), never full-page.
5. On the next nightly run, confirm all 29 specs in `frontend/test/e2e/issued-invoices/` pass.

## Notes

- No deviations from the task spec. The call-site edit places the new `{ enabled: activeTab === 'grid' }`
  argument on the same line as the closing `}` of the filters object (`}, { enabled: activeTab ===
  'grid' });`) rather than the multi-line form shown in the task's illustrative snippet — purely
  cosmetic, functionally identical, and confirmed lint-clean.
- The diff is scoped to exactly the two files named in the task (`useIssuedInvoices.ts`,
  `IssuedInvoicesPage.tsx`); `git diff --stat` on the `frontend/` path shows only those two files,
  no unrelated formatting changes.
- `artifacts/feat-3541/state.json` was modified by the surrounding pipeline tooling (not by this
  implementation) and was deliberately left unstaged, per instructions to scope `git add` to
  `frontend/` only.

## Status
DONE
