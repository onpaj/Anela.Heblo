# Architecture Review: Fix Batch-Planning Error-Handling E2E Tests

## Skip Design: true
This is a test-only defect with zero application/product code impact. No new or changed UI
components, screens, layouts, or visual decisions are involved. The fix realigns navigation,
heading assertion, and load-wait strategy inside one Playwright spec file so it reliably
reaches the existing `/manufacturing/batch-planning` page. No design work is required.

## Architectural Fit Assessment

The proposed fix aligns cleanly with the existing E2E conventions and I verified every load-bearing
claim in the spec against the actual codebase:

- **Sidebar labels/routes** (`frontend/src/components/Layout/Sidebar.tsx`): the "Výroba" section contains
  `id: "kalkulator-davek"` / name **"Kalkulačka dávek"** → `/manufacturing/batch-calculator` (lines 233-236),
  listed **before** `id: "planovani-davek"` / name **"Plánování dávek"** → `/manufacturing/batch-planning`
  (lines 239-242). Confirmed.
- **Routing** (`frontend/src/App.tsx` lines 420-421): both routes exist and are guarded (`guard(...)`);
  `BatchPlanningCalculator` is the planning page component, `ManufactureBatchCalculator` the calculator.
- **Headings**: planning page `<h1>` = **"Plánovač výrobních dávek"** (`ManufactureBatchPlanning.tsx:505-506`);
  calculator page `<h1>` = **"Kalkulačka dávek pro výrobu"** (`ManufactureBatchCalculator.tsx:247`).
- **The defect is real**: the failing selector `text=/Plánovač|Kalkulačka dávek/i` cannot match the real
  link text "Plánování dávek" (the alternatives are `Plánovač` — suffix *-ovač* not *-ování* — and the
  literal `Kalkulačka dávek`). The only sidebar item it matches is "Kalkulačka dávek", which routes to the
  wrong page. The loose heading regex `/Plánovač|Planning|Dávek|Kalkulačka/i` then fails to catch the
  wrong-page landing (it would even accept "Kalkulačka…").
- **The reference is proven**: the passing `batch-planning-workflow.spec.ts` navigates via
  `getByRole('link', { name: /plánovač výrobních dávek|plánování dávek/i })`, asserts the exact `<h1>`, and
  waits on `domcontentloaded`. It uses the same `TestCatalogItems.hedvabnyPan` (MAS001001M) fixture, proving
  the data exists on staging.
- **Fixture** (`frontend/test/e2e/fixtures/test-data.ts:70-73`): `hedvabnyPan = { code: 'MAS001001M',
  name: 'Hedvábný pan Jasmín', type: 'Polotovar' }`. Confirmed.
- **Module placement** (`docs/testing/e2e-module-guide.md`): `batch-planning-error-handling.spec.ts` already
  lives in the `manufacturing/` module and is registered there (line 118). No relocation needed.

The fix is a "copy the proven sibling" change — the safest possible class of E2E fix. There is no
architectural tension; it *reduces* drift between the two manufacturing specs.

## Proposed Architecture

### Component Overview

```
navigateToApp(page)  ──►  Entra auth + frontend session  (helpers/e2e-auth-helper.ts)
        │
        ▼
Sidebar "Výroba" button ──click──► "Plánování dávek" link ──► /manufacturing/batch-planning
        │                                                              │
        │  (BROKEN today: text=/Plánovač|Kalkulačka dávek/i            ▼
        │   matches "Kalkulačka dávek" → /manufacturing/batch-      BatchPlanningCalculator
        │   calculator, the WRONG page)                             <h1>Plánovač výrobních dávek</h1>
        ▼                                                           [role=combobox] (CatalogAutocomplete)
   heading assertion  ──►  h1 filter /plánovač výrobních dávek/i    <table> product rows
        │                  (must be exact, fail loudly)             "Přepočítat" button
        ▼
   combobox → select MAS001001M → product table → check fixed → set qty → Přepočítat → assert
```

The only components that change are the two `test(...)` blocks and the `beforeEach` inside
`frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts`.

### Key Design Decisions

#### Decision 1: Mirror the passing workflow spec's navigation, not invent a new one
**Options considered:**
(a) Fix only the regex in the existing `text=` selector; (b) Replace navigation with the role-based
approach used by the passing `batch-planning-workflow.spec.ts`; (c) Navigate exclusively via
`page.goto('/manufacturing/batch-planning')`.
**Chosen approach:** (b) — `getByRole('button', { name: 'Výroba' }).click()` then
`getByRole('link', { name: /plánování dávek/i }).click()` (optionally also `/plánovač výrobních dávek/i`
for parity). Keep a `goto('/manufacturing/batch-planning')` only as a genuine fallback when the link is
absent.
**Rationale:** Option (b) is already proven on staging by the sibling spec and exercises the real user
navigation path (which the error-handling test's *intent* is to cover). Role + accessible-name is resilient
to unrelated text changes (NFR-3). Option (a) leaves a fragile broad `text=` regex that can cross-match the
sibling page again; option (c) would silently stop testing the sidebar, weakening coverage.

#### Decision 2: Tighten the heading assertion to the exact page title
**Options considered:** Keep the permissive `h1, h2` + `/Plánovač|Planning|Dávek|Kalkulačka/i`;
or use `page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })`.
**Chosen approach:** The exact `h1` filter, identical to the workflow spec.
**Rationale:** The loose regex is precisely what masked this bug — it would pass on the calculator page.
An exact assertion turns a wrong-page regression into an immediate, legible failure at the navigation step
rather than a confusing downstream combobox/timeout failure.

#### Decision 3: Replace `networkidle` with `domcontentloaded`
**Options considered:** Keep `waitForLoadState('networkidle')`; switch to `'domcontentloaded'`.
**Chosen approach:** `'domcontentloaded'` in `beforeEach` and post-navigation, matching the workflow spec.
**Rationale:** Staging emits background telemetry/polling that keeps the network busy, making `networkidle`
flaky and slow. The proven spec uses `domcontentloaded` + explicit element visibility waits, which is the
correct gating mechanism. Visibility assertions (`toBeVisible({ timeout })`) already provide the real
synchronization.

#### Decision 4: Treat FR-6 (shared nav helper) as optional and deferred
**Chosen approach:** Do not block the fix on extracting a `navigateToBatchPlanning(page)` helper.
**Rationale:** The spec marks it optional. The existing `helpers/` directory (e.g. `e2e-auth-helper.ts`,
`wait-helpers.ts`) is the correct home *if* it is done, but introducing shared state across two specs is a
larger change than this bug warrants. Recommend deferring; if implemented, it must be adopted by both
error-handling tests and leave the workflow spec's behavior unchanged.

## Implementation Guidance

### Directory / Module Structure
- **Only file to change:** `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts`.
- No new files required. Do **not** create a helper unless FR-6 is explicitly adopted; if so, it belongs at
  `frontend/test/e2e/helpers/` and must be imported with the `../` prefix (per module guide §"Use Correct
  Import Paths").
- Do **not** touch: `ManufactureBatchPlanning.tsx`, `ManufactureBatchCalculator.tsx`, `Sidebar.tsx`,
  `App.tsx`, `CatalogAutocomplete.tsx`, `test-data.ts`, `playwright.config.ts`, or the passing
  `batch-planning-workflow.spec.ts`.

### Interfaces and Contracts
The spec must conform to these existing, verified landmarks (all confirmed present):
- Navigation: section button accessible name `'Výroba'`; link accessible name matching `/plánování dávek/i`.
- Page identity: `page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })`.
- Product selection: `[role="combobox"]` (rendered by `CatalogAutocomplete` via react-select), then
  `[role="option"]` results, then `tbody tr` rows > 0.
- Recalculation trigger: `button` with text `/Přepočítat|Calculate|Vypočítat/i`.
- Auth: continue via `navigateToApp(page)` (CLAUDE.md rule — never `createE2EAuthSession()` alone).
- Fixture: `TestCatalogItems.hedvabnyPan` (MAS001001M). Keep the existing `throw` (not `test.skip`) when no
  options appear, per the project's "throw on missing fixture data" rule.

### Data Flow
For both tests the corrected flow is: `navigateToApp` (auth + session) → click "Výroba" → click
"Plánování dávek" link → `waitForLoadState('domcontentloaded')` → assert exact `<h1>` → open combobox →
type "Hedvábný pan Jasmín" → select first `[role="option"]` → product `<table>` + `tbody tr` populate →
check fixed checkbox(es) → set quantity (`9999` over-volume for test 1; `10` reasonable for test 2) →
click "Přepočítat" → assert (test 1: table/rows remain visible, toaster checks stay logged-only; test 2:
zero error toasters via `.toast, .Toastify__toast, [role="alert"]` filtered by error text). Behavioral
assertions are preserved exactly (FR-5); only navigation, page-load gating, and wait strategy change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| The two staging manufacturing menu items are permission-gated (`RequireMenuPath`); if the E2E principal lacks the planning menu path, the link won't render and the test still fails. | Medium | The passing workflow spec navigates to the same route with the same auth, proving the link renders for the E2E user. If it regresses, the exact-heading assertion fails loudly at the navigation step, making diagnosis immediate. |
| Retaining a `goto('/manufacturing/batch-planning')` fallback could again mask a broken link click. | Low | Only invoke the fallback when the link is genuinely absent (link `isVisible` false), never as a catch for having clicked the wrong link (FR-1 acceptance criteria). |
| Test 2 (line 248) currently lacks a heading guard before touching the combobox. | Low | Add the same exact-`h1` visibility assertion after navigation in test 2 so a wrong page fails before the combobox step (FR-2). |
| Removing `networkidle` could race the combobox on slow staging. | Low | Rely on `toBeVisible({ timeout: 10000 })` element waits already present; keep the modest `waitForTimeout` React-init pauses the workflow spec uses. |
| Both manufacturing pages expose `[role="combobox"]`; a future wrong-page navigation would still find *a* combobox. | Low | The exact-heading assertion (Decision 2) gates the page before any combobox interaction, so a wrong page cannot reach the combobox step. |

## Specification Amendments
None required — the spec is accurate and complete. Two clarifications for the implementer:
1. **FR-2 for test 2:** make explicit that the exact-`h1` guard is *added* to the correction test (line 248),
   which today has no page-loaded assertion before the combobox. The spec mentions this ("should add/keep an
   equivalent page-loaded guard") — treat it as required, not optional.
2. **FR-1 navigation regex:** prefer `/plánování dávek/i` as the primary link matcher (it matches the real
   label). Including `/plánovač výrobních dávek/i` as an alternative is harmless parity with the workflow
   spec but is not strictly needed, since that string is the page `<h1>`, not the sidebar link text.

## Prerequisites
None beyond the existing E2E environment. Specifically:
- Staging (`https://heblo.stg.anela.cz`) reachable with valid E2E auth (`E2E_CLIENT_ID` / `E2E_CLIENT_SECRET`
  / `AZURE_TENANT_ID`) and the `MAS001001M` semiproduct present with configured products — all already
  satisfied (proven by the passing workflow spec).
- No migrations, config, feature flags, or infrastructure changes.
- Validation per CLAUDE.md: `npm run lint` (frontend) plus the E2E run
  `./scripts/run-playwright-tests.sh manufacturing` against staging; both error-handling tests must pass and
  the sibling workflow spec must remain green.
