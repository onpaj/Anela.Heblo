# E2E Test Status Map

> **Purpose**: Orchestration tracking for the "make E2E tests usable" effort.
> **Target**: deployed staging `https://heblo.stg.anela.cz` (nightly gate, not PR CI).
> **Run a single test**: `./scripts/run-playwright-tests.sh <module> "<test name>"`

## Legend

| Mark | Meaning |
| ---- | ------- |
| ⬜ | Not yet triaged |
| ✅ | Passing (verified) |
| 🔧 | Test was invalid → fixed |
| 🐛 | **Valid test exposing a real application bug** — test left failing/skipped, bug NOT fixed (out of scope) |
| 🗑️ | Test no longer meaningful → deleted |
| ⏭️ | Pre-existing `.skip` inherited from earlier effort |

## Fixed root causes

### Changelog toaster intercepted clicks (57 of 81 baseline failures)

The changelog toaster renders `fixed top-4 right-4 z-50` and deliberately never
auto-hides (`useChangelogToaster`: _"Do not start auto-hide - user must close manually"_).
Every Playwright test starts with empty `localStorage`, so `isNewVersion()` was always
true and the toaster stayed pinned over the filter/action controls for the whole test.
Playwright's click log: `subtree intercepts pointer events / 14 × retrying click action`.

Because helpers do `Promise.all([responsePromise, button.click()])`, the 30 s
`waitForResponse` rejected first and **masked** the real click failure — which is why
the symptom looked like a dead API endpoint. `/api/catalog` was correct and returning 200
all along.

Fixed in tests (not app — the toaster is intended behavior for real users) via
`helpers/changelog-toaster-helper.ts`, called from `navigateToApp()`.

## Application bugs found (do not fix — report only)

| # | Area | Symptom | Status |
| - | ---- | ------- | ------ |
| 1 | catalog filter | `/api/catalog?productName=Kr%C3%A9m` → HTTP 200 but **0 rows**, while unfiltered returns 20. Possible diacritics/case handling bug in backend query. | ⚠️ **Unverified** — may just be staging data. Needs confirmation. |
| 2 | `GET /api/e2etest/auth-status` | Returns **HTTP 400** `"An item with the same key has already been added. Key: .../claims/role"`. `E2ETestController.cs:150` does `User.Claims.ToDictionary(c => c.Type, c => c.Value)`; `ToDictionary` throws on duplicate keys and the E2E identity carries 11+ `ClaimTypes.Role` claims (`E2ESessionService.CreateSyntheticUserClaims`), plus one per permission from `PermissionClaimsTransformation`. Endpoint has been unreachable since the 2nd role claim landed. Fix: group by type into `string[]`. | 🐛 **Confirmed**, not fixed. Test `core/staging-auth.spec.ts` "should validate API authentication status" marked `.skip()` with pointer. |

## Test-quality debt noted (not blocking)

- `core/dashboard.spec.ts` "should support drag and drop to reorder tiles" is **destructive**: it persists a new tile order for the shared staging E2E user via `useSaveDashboardSettings`, so repeated runs keep shuffling that user's dashboard.
- `core/dashboard.spec.ts:53` still uses a bare `waitForTimeout(1000)` — passes today, same latent-flake class as the ones removed.
- `core/changelog.spec.ts` "should display version history in modal" is **flaky**: observed failing twice standalone (both with and without the toaster fix) but passing in a full-module run. Needs a stability check.

---

## Module status

| Module | Result | Detail |
| ------ | ------ | ------ |
| catalog | ✅ **green** | 84 passed, 3 skipped, 0 failed (was ~70 failing pre-toaster-fix) |
| core | ✅ **green** | 72 passed, 8 skipped, 0 failed |
| baleni | ⬜ | baseline in progress |
| finance | ⬜ | baseline in progress |
| issued-invoices | ⬜ | 9 failures in partial baseline; re-measuring post-toaster-fix |
| leaflet-generator | ⬜ | baseline in progress |
| manufacturing | ⬜ | baseline in progress |
| marketing | ⬜ | baseline in progress |
| stock-operations | ⬜ | 2 failures in partial baseline; re-measuring post-toaster-fix |
| terminal | ⬜ | baseline in progress |
| transport | ⬜ | baseline in progress |

---

## Per-file tracking

Filled in from the baseline run.

| Spec file | Result | Notes |
| --------- | ------ | ----- |
| baleni/packing.spec.ts | ⬜ | |
| catalog/clear-filters.spec.ts | ✅ | |
| catalog/combined-filters.spec.ts | ✅ | |
| catalog/filter-edge-cases.spec.ts | ✅ | |
| catalog/margins-chart.spec.ts | ✅ | |
| catalog/pagination-with-filters.spec.ts | ✅ | |
| catalog/product-type-filter.spec.ts | ✅ | |
| catalog/sorting-with-filters.spec.ts | ✅ | |
| catalog/text-search-filters.spec.ts | ✅ | |
| catalog/ui.spec.ts | ✅ | |
| core/changelog.spec.ts | ⚠️ | 9/10 pass. FAIL: `should display version history in modal` — pre-existing (fails with and without toaster fix). Needs triage. |
| core/dashboard.spec.ts | ⚠️ | FAIL: `should display AutoShow tiles automatically`, `should support drag and drop to reorder tiles` |
| core/invoice-classification-history-actions.spec.ts | ✅ | passing (some inherited `.skip`) |
| core/invoice-classification-history-filters.spec.ts | ✅ | passing (some inherited `.skip`) |
| core/invoice-classification-history.spec.ts | ✅ | |
| core/recurring-jobs-management.spec.ts | ✅ | |
| core/sidebar-navigation.spec.ts | ⚠️ | FAIL: `should display Anela section before Sklad and Administrace` |
| core/staging-auth.spec.ts | ⚠️ | FAIL: `should validate API authentication status` |
| finance/financial-overview-mobile.spec.ts | ⬜ | |
| issued-invoices/filters.spec.ts | ⬜ | |
| issued-invoices/navigation.spec.ts | ⬜ | |
| issued-invoices/pagination.spec.ts | ⬜ | |
| issued-invoices/sorting.spec.ts | ⬜ | |
| issued-invoices/status-badges.spec.ts | ⬜ | |
| leaflet-generator/leaflet-doc-management.spec.ts | ⬜ | |
| manufacturing/batch-planning-error-handling.spec.ts | ⬜ | |
| manufacturing/batch-planning-workflow.spec.ts | ⬜ | |
| manufacturing/order-creation.spec.ts | ⬜ | |
| manufacturing/order-state-return.spec.ts | ⬜ | |
| manufacturing/protocol.spec.ts | ⬜ | |
| marketing/calendar-view.spec.ts | ⬜ | |
| marketing/create-record.spec.ts | ⬜ | |
| marketing/grid-view.spec.ts | ⬜ | |
| marketing/loading.spec.ts | ⬜ | |
| marketing/mobile-agenda.spec.ts | ⬜ | |
| stock-operations/accept.spec.ts | ⬜ | |
| stock-operations/badges.spec.ts | ⬜ | |
| stock-operations/filters.spec.ts | ⬜ | |
| stock-operations/navigation.spec.ts | ⬜ | |
| stock-operations/panel.spec.ts | ⬜ | |
| stock-operations/retry.spec.ts | ⬜ | |
| stock-operations/sorting.spec.ts | ⬜ | |
| stock-operations/source-filter.spec.ts | ⬜ | |
| stock-operations/state-filter.spec.ts | ⬜ | |
| terminal/lot-identification.spec.ts | ⬜ | |
| transport/box-creation.spec.ts | ⬜ | |
| transport/box-items.spec.ts | ⬜ | |
| transport/box-management.spec.ts | ⬜ | |
| transport/box-receive.spec.ts | ⬜ | |
| transport/box-workflow.spec.ts | ⬜ | |
| transport/boxes-basic.spec.ts | ⬜ | |
| transport/ean-integration.spec.ts | ⬜ | |
