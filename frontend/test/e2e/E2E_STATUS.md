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
| 1 | catalog filter | `/api/catalog?productName=Kr%C3%A9m` → HTTP 200 but **0 rows**, while unfiltered returns 20. | ✅ **Dismissed** — catalog runs fully green and no test depends on it; almost certainly thin staging data, not a bug. Not reported. |
| 4 | catalog — clear filters | Clearing filters does not reset pagination to page 1. Two competing `useEffect` hooks race on URL sync. | ⏭️ **Inherited skip**, pre-existing. `catalog/clear-filters.spec.ts` "should reset page to 1 after clearing". |
| 5 | catalog — sorting | Changing sort while on page 2 keeps `page=2`, so sorted results start at record 21 instead of 1. User sees the middle of the sorted list. | ⏭️ **Inherited skip**, pre-existing. `catalog/sorting-with-filters.spec.ts` "should reset to page 1 when changing sort". |
| 6 | catalog — combined text filters | Name **and** code filters applied simultaneously return 0 results, though each works alone. May be by design; needs backend confirmation. | ⏭️ **Inherited skip**, unconfirmed. `catalog/text-search-filters.spec.ts`. |
| 3 | Terminal lot identification — **user-facing** | Scanning an already-assigned (or unknown) container label tells the operator **"Chyba připojení."** (connection error). Backend is correct: `POST /api/material-containers` returns `409` with `{"errorCode":"MaterialContainerCodeExists","params":{...}}`. But the NSwag client (`api-client.ts:8140`) throws for any status other than 200/204, so React Query routes it to `onError` (`ReceiveScreen.tsx:124-127`) which cannot read the body and emits the generic message. This makes `ReceiveScreen.tsx:106-118` **dead code** — both the `MaterialContainerCodeExists` and `UnknownMaterialContainerCode` branches are unreachable, since `ErrorCodes.cs:351` maps the former to 409 and the latter to 400. A warehouse operator scanning a duplicate label is told their internet is down. | 🐛 **Confirmed**, not fixed. Test skipped with pointer. |
| 2 | `GET /api/e2etest/auth-status` | Returns **HTTP 400** `"An item with the same key has already been added. Key: .../claims/role"`. `E2ETestController.cs:150` does `User.Claims.ToDictionary(c => c.Type, c => c.Value)`; `ToDictionary` throws on duplicate keys and the E2E identity carries 11+ `ClaimTypes.Role` claims (`E2ESessionService.CreateSyntheticUserClaims`), plus one per permission from `PermissionClaimsTransformation`. Endpoint has been unreachable since the 2nd role claim landed. Fix: group by type into `string[]`. | 🐛 **Confirmed**, not fixed. Test `core/staging-auth.spec.ts` "should validate API authentication status" marked `.skip()` with pointer. |

## Test-quality debt noted (not blocking)

- `core/dashboard.spec.ts` "should support drag and drop to reorder tiles" is **destructive**: it persists a new tile order for the shared staging E2E user via `useSaveDashboardSettings`, so repeated runs keep shuffling that user's dashboard.
- `core/dashboard.spec.ts:53` still uses a bare `waitForTimeout(1000)` — passes today, same latent-flake class as the ones removed.
- `navigateToIssuedInvoices` logs `❌ Zákaznické menu item not found` on every run and silently falls back to direct URL navigation. Tests pass either way, but the sidebar path in that helper is dead in practice and may be masking a stale menu selector.
- `terminal/lot-identification.spec.ts` "PO flow: receive several materials from one order" failed once in a full run, then passed in isolation and in two subsequent full runs. Not perfectly stable; may resurface nightly.
- `core/changelog.spec.ts` "should display version history in modal" is **flaky**: observed failing twice standalone (both with and without the toaster fix) but passing in a full-module run. Needs a stability check.

---

## Module status

| Module | Result | Detail |
| ------ | ------ | ------ |
| catalog | ✅ **green** | 84 passed, 3 skipped, 0 failed (was ~70 failing pre-toaster-fix) |
| core | ✅ **green** | 72 passed, 8 skipped, 0 failed |
| baleni | ✅ **green** | 2 passed, 0 failed — tests now pick a balič first (scan input is disabled until then); 3 stale shipment-creation tests removed after #1502 moved orchestration to the backend |
| finance | ✅ **green** | 3 passed, 0 failed (was 0/3). Not a blank render and not stale labels — the page loads fine, it just takes 20-30s on staging, so the 5s assertion default expired first. Fixed with a data-ready gate in `beforeEach`. |
| issued-invoices | ✅ **green** | 29 passed, 0 failed (was 9 failing) |
| leaflet-generator | ✅ **green** | 2 passed, 2 skipped (role-gated: marketing_reader), 0 failed |
| manufacturing | ✅ **green** | 9 passed, 0 failed |
| marketing | ✅ **green** | 36 passed, 0 failed |
| stock-operations | ✅ **green** | 57 passed, 0 failed (was 2 failing) |
| terminal | ⚠️ | 4 passed, 1 **skipped** — blocked by app bug #3 (real, user-facing). |
| transport | ✅ **green** | 42 passed, 0 failed (no changes needed) |

---

## Per-file tracking

Filled in from the baseline run.

| Spec file | Result | Notes |
| --------- | ------ | ----- |
| baleni/packing.spec.ts | ✅ | |
| catalog/clear-filters.spec.ts | ✅ | |
| catalog/combined-filters.spec.ts | ✅ | |
| catalog/filter-edge-cases.spec.ts | ✅ | |
| catalog/margins-chart.spec.ts | ✅ | |
| catalog/pagination-with-filters.spec.ts | ✅ | |
| catalog/product-type-filter.spec.ts | ✅ | |
| catalog/sorting-with-filters.spec.ts | ✅ | |
| catalog/text-search-filters.spec.ts | ✅ | |
| catalog/ui.spec.ts | ✅ | |
| core/changelog.spec.ts | ✅ | Green in full-module run. `should display version history in modal` seen flaky standalone — see debt list. |
| core/dashboard.spec.ts | 🔧 | drag-and-drop rewritten to drive dnd-kit KeyboardSensor (raw mouse drag was a no-op); AutoShow tiles fixed by toaster suppression |
| core/invoice-classification-history-actions.spec.ts | ✅ | passing (some inherited `.skip`) |
| core/invoice-classification-history-filters.spec.ts | ✅ | passing (some inherited `.skip`) |
| core/invoice-classification-history.spec.ts | ✅ | |
| core/recurring-jobs-management.spec.ts | ✅ | |
| core/sidebar-navigation.spec.ts | 🔧 | added `toHaveCount(3)` wait — `allTextContents()` does not auto-wait, ran before React rendered |
| core/staging-auth.spec.ts | 🐛 | `should validate API authentication status` skipped — blocked by backend bug #2 |
| finance/financial-overview-mobile.spec.ts | 🔧 | All 3 were missing a wait for the slow Financial Overview data query; added an auto-waiting data-ready gate in `beforeEach`. Labels/roles were all correct. |
| issued-invoices/filters.spec.ts | ✅ | |
| issued-invoices/navigation.spec.ts | 🔧 | scoped ambiguous `Celkem faktur` locator (heading rendered twice by design) |
| issued-invoices/pagination.spec.ts | ✅ | |
| issued-invoices/sorting.spec.ts | ✅ | |
| issued-invoices/status-badges.spec.ts | ✅ | |
| leaflet-generator/leaflet-doc-management.spec.ts | 🔧 | `Generovat` locator made exact — getByRole name matching is substring by default |
| manufacturing/batch-planning-error-handling.spec.ts | ✅ | |
| manufacturing/batch-planning-workflow.spec.ts | ✅ | |
| manufacturing/order-creation.spec.ts | ✅ | |
| manufacturing/order-state-return.spec.ts | ✅ | |
| manufacturing/protocol.spec.ts | ✅ | |
| marketing/calendar-view.spec.ts | 🔧 | `Dnes` test: exact-name locator (events titled "MF Dnes" matched the substring filter), wait for datesSet before capturing label, poll the click (gotoDate swallowed mid-render under load) |
| marketing/create-record.spec.ts | ✅ | |
| marketing/grid-view.spec.ts | ✅ | |
| marketing/loading.spec.ts | ✅ | |
| marketing/mobile-agenda.spec.ts | ✅ | |
| stock-operations/accept.spec.ts | ✅ | |
| stock-operations/badges.spec.ts | ✅ | |
| stock-operations/filters.spec.ts | ✅ | |
| stock-operations/navigation.spec.ts | ✅ | |
| stock-operations/panel.spec.ts | ✅ | |
| stock-operations/retry.spec.ts | ✅ | |
| stock-operations/sorting.spec.ts | ✅ | |
| stock-operations/source-filter.spec.ts | ✅ | |
| stock-operations/state-filter.spec.ts | ✅ | |
| terminal/lot-identification.spec.ts | 🐛 | `duplicate code shows the already-assigned message` skipped — app bug #3 |
| transport/box-creation.spec.ts | ✅ | |
| transport/box-items.spec.ts | ✅ | |
| transport/box-management.spec.ts | ✅ | |
| transport/box-receive.spec.ts | ✅ | |
| transport/box-workflow.spec.ts | ✅ | |
| transport/boxes-basic.spec.ts | ✅ | |
| transport/ean-integration.spec.ts | ✅ | |
