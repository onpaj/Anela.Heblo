# Architecture Review: Fix Stock-Operations E2E Navigation URL

## Skip Design: true

## Architectural Fit Assessment

This change is entirely within the E2E test layer. No application source files, API contracts, backend handlers, or React components are touched. The fix is a mechanical string substitution correcting a typo that has caused every test in the `stock-operations` Playwright module to fail since the suite was written.

The application route `/stock-up-operations` is registered at `frontend/src/App.tsx:449` and linked from the sidebar at `frontend/src/components/Layout/Sidebar.tsx:345`. Both have always used the correct path. The test code was written with the truncated string `/stock-operations` and no redirect or catch-all exists to absorb the mismatch, so every navigation lands on a blank page.

Integration points:
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — the `navigateToStockOperations` function, which all spec `beforeEach` blocks call.
- `frontend/test/e2e/stock-operations/*.spec.ts` — eight spec files, seven of which assert `page.url().toContain('/stock-operations')`. One (`navigation.spec.ts`) also contains a bare `page.goto` with the wrong path.
- `frontend/test/e2e/helpers/stock-operations-test-helpers.ts` — no URL references; not changed.

## Proposed Architecture

### Component Overview

```
E2E test execution
│
├── beforeEach (all spec files)
│   └── navigateToStockOperations()               [e2e-auth-helper.ts:262]
│       └── page.goto(`${baseUrl}/stock-operations`)   ← WRONG, fix here
│
├── navigation.spec.ts
│   ├── line 13  expect(page.url()).toContain('/stock-operations')   ← fix
│   └── line 96  page.goto(`${baseUrl}/stock-operations`)           ← fix
│
└── URL assertions in 7 spec files
    ├── badges.spec.ts:15     toContain('/stock-operations')         ← fix
    ├── accept.spec.ts:13     toContain('/stock-operations')         ← fix
    ├── state-filter.spec.ts:14   toContain('/stock-operations')     ← fix
    ├── source-filter.spec.ts:13  toContain('/stock-operations')     ← fix
    ├── sorting.spec.ts:14    toContain('/stock-operations')         ← fix
    ├── retry.spec.ts:15      toContain('/stock-operations')         ← fix
    └── panel.spec.ts:18      toContain('/stock-operations')         ← fix

filters.spec.ts — no URL string references, no change required
```

### Key Design Decisions

#### Decision 1: Correct the string at all callsites — do not add a redirect

**Options considered:**
1. Add a React Router `<Redirect from="/stock-operations" to="/stock-up-operations" />` in `App.tsx` and leave the test URLs alone.
2. Fix only the `navigateToStockOperations` helper (centralised navigation path) and leave spec-level assertions.
3. Fix every wrong string in test files; no application change.

**Chosen approach:** Option 3 — fix only test files.

**Rationale:** The spec explicitly rules out adding a redirect (Out of Scope). A redirect would paper over a broken test by making a non-existent path work in production, which is undesirable. The URL assertions in spec files serve as a sanity check that the browser landed on the right page; they must assert the real route (`/stock-up-operations`), not a redirect source. Option 2 would leave seven broken URL assertions still failing. The correct fix is surgical: update every wrong occurrence in the test layer only.

#### Decision 2: Replace the full substring, not just the prefix

**Options considered:**
1. Change `/stock-operations` to `/stock-up-operations` everywhere (exact substring match).
2. Use a regex assertion (`/stock.*operations/`) to be resilient to future renames.

**Chosen approach:** Option 1 — exact string replacement.

**Rationale:** The assertions must verify the precise route the app uses. A loose regex would survive a future route rename silently and defeat the purpose of URL assertions. Use the real path string throughout.

## Implementation Guidance

### Directory / Module Structure

No new files. No deleted files. Changes are confined to:

```
frontend/test/e2e/
├── helpers/
│   └── e2e-auth-helper.ts          (1 occurrence, line 270)
└── stock-operations/
    ├── navigation.spec.ts          (2 occurrences, lines 13 and 96)
    ├── badges.spec.ts              (1 occurrence, line 15)
    ├── accept.spec.ts              (1 occurrence, line 13)
    ├── state-filter.spec.ts        (1 occurrence, line 14)
    ├── source-filter.spec.ts       (1 occurrence, line 13)
    ├── sorting.spec.ts             (1 occurrence, line 14)
    ├── retry.spec.ts               (1 occurrence, line 15)
    └── panel.spec.ts               (1 occurrence, line 18)
```

`filters.spec.ts` contains no URL string; confirmed by grep — no change required.

### Interfaces and Contracts

No interface or type changes. The public signature of `navigateToStockOperations` is unchanged; callers are unaffected.

### Data Flow

Before fix:
```
beforeEach → navigateToStockOperations → page.goto(.../stock-operations)
           → React Router: no match → blank page
           → waitForTableUpdate → timeout at 15 s → test failure
```

After fix:
```
beforeEach → navigateToStockOperations → page.goto(.../stock-up-operations)
           → React Router: matches /stock-up-operations → StockOperationsPage renders
           → waitForTableUpdate → tbody tr or "Žádné výsledky" visible → test continues
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A grep miss leaves a stray `/stock-operations` occurrence | Medium | After edits, run `grep -r '/stock-operations' frontend/test/e2e/` and confirm zero results |
| `navigation.spec.ts` line 96 uses `PLAYWRIGHT_BASE_URL` instead of `PLAYWRIGHT_FRONTEND_URL` — environment variable mismatch could leave the URL blank | Low | Line 96 reads `process.env.PLAYWRIGHT_BASE_URL \|\| 'https://heblo.stg.anela.cz'`; the helper at line 270 reads `PLAYWRIGHT_FRONTEND_URL \|\| PLAYWRIGHT_BASE_URL \|\| 'https://heblo.stg.anela.cz'`. Make line 96 consistent with the helper's env-var fallback chain when fixing |
| Staging data may be in a state that produces 0 rows, causing `waitForTableUpdate` to wait for `h3[Žádné výsledky]` — this is correct by design but requires staging to be reachable | Low | Verify staging is accessible before running the E2E suite |

## Specification Amendments

The spec lists the following spec files as containing URL assertions (FR-4): badges.spec.ts, accept.spec.ts, state-filter.spec.ts, source-filter.spec.ts, sorting.spec.ts, retry.spec.ts, panel.spec.ts. This is accurate. `filters.spec.ts` is correctly omitted — confirmed by grep.

One clarification beyond the spec: `navigation.spec.ts` line 96 uses `process.env.PLAYWRIGHT_BASE_URL` as its primary env variable, while the `navigateToStockOperations` helper (line 270) prefers `PLAYWRIGHT_FRONTEND_URL` first. The fix for line 96 should align it with the helper's fallback chain (`PLAYWRIGHT_FRONTEND_URL || PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'`) to avoid a silent environment-variable discrepancy in the same file.

## Prerequisites

- Staging environment reachable at `https://heblo.stg.anela.cz` with stock-operations test data present.
- No migrations, config changes, or infrastructure work required.
- `./scripts/run-playwright-tests.sh` must target staging (default) and be executable.
