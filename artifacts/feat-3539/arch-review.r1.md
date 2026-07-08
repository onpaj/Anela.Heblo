# Architecture Review: Fix Catalog Page Navigation E2E Regression (navigateToCatalog)

## Skip Design: true

## Architectural Fit Assessment

This is a test-infrastructure bug confined to a single helper function, `navigateToCatalog`, in `frontend/test/e2e/helpers/e2e-auth-helper.ts`. It does not touch product code, routing, or UI. The fix should adopt a pattern that already exists twice in the exact same file (`navigateToTransportBoxes`, lines 178-232; `navigateToTransportBoxReceive`, lines 277-319), so there is no new architecture to design — only a structural correction to bring one outlier helper in line with its siblings. I confirmed by reading the file directly that:

- `navigateToCatalog` (lines 234-260) is the only UI-driving helper whose fallback lives inside `catch` rather than unconditionally after `try/catch`.
- It is also the only one still using 2000ms `isVisible` timeouts; every other UI-driving helper (`navigateToTransportBoxes`, `navigateToTransportBoxReceive`, `navigateToInvoiceClassification`, `navigateToIssuedInvoices`, `navigateToMarketingCalendar`) uses 5000ms.
- None of the working helpers self-verify the end URL by throwing — `navigateToMarketingCalendar` gets closest by waiting on an `h1` heading with a 15s timeout on both paths, which is a reasonable model for "confirm the destination was reached" but not literally a URL check. `navigateToCatalog`'s spec explicitly requires a URL-based self-check with a descriptive throw, which is new within this file (first helper to do so) but consistent with FR-1/NFR-2 in the spec and doesn't conflict with any existing pattern.

No other module is affected. The 9 catalog spec files (`frontend/test/e2e/catalog/*.spec.ts`) call `navigateToCatalog(page)` and then independently assert `page.url()`; per the spec this redundant assertion may remain (harmless once the helper self-verifies) — removing it is optional cleanup, not required.

## Proposed Architecture

No new components. Single-function restructuring inside the existing helper module.

### Component Overview

```
frontend/test/e2e/catalog/*.spec.ts (9 files)
        │  calls
        ▼
navigateToCatalog(page)  [frontend/test/e2e/helpers/e2e-auth-helper.ts]
        │
        ├─ navigateToApp(page)               (unchanged — auth + app shell mount)
        ├─ UI path: click "Produkty" → click "Katalog"   (isVisible timeout 2000→5000ms)
        ├─ fallback: page.goto(`${baseUrl}/catalog`)      (moved outside catch, unconditional)
        └─ self-check: throw if page.url() doesn't contain '/catalog'  (new)
```

### Key Design Decisions

#### Decision 1: Fallback placement — outside try/catch, gated by early return on UI success

**Options considered:**
- (a) Keep fallback in `catch`, additionally check the result of `isVisible()` and manually invoke the fallback logic in the `if`-false branches too (duplicating the goto/wait code at three call sites within the function).
- (b) Restructure to match `navigateToTransportBoxes`/`navigateToTransportBoxReceive`: attempt UI navigation inside `try`, `return` immediately once the UI path is confirmed to have landed on `/catalog`, and — for every other path, whether via a timeout-driven `isVisible() === false`, an exception, or a UI click that didn't actually route to `/catalog` — fall through to a single direct-navigation block placed unconditionally after the `try/catch`.

**Chosen approach:** (b), matching the two existing correct helpers verbatim in structure.

**Rationale:** This is a one-function bug fix in a file with an established, working pattern used by two sibling helpers. Duplicating fallback logic per branch (option a) would diverge from that pattern for no benefit and add more places for the same class of bug to recur. Reusing the sibling pattern also means the "early return only on confirmed success" idiom is uniform across the file, which is directly what the spec's FR-1 item 1 asks for ("mirroring the pattern already used by `navigateToTransportBoxes`/`navigateToTransportBoxReceive`").

#### Decision 2: Where "confirmed success" is checked before the early return

**Options considered:**
- (a) Return early as soon as the "Katalog" click resolves, without checking the URL (this is closer to what `navigateToTransportBoxReceive` does — it returns after the click succeeds, trusting the click).
- (b) Explicitly check `page.url().includes('/catalog')` after the UI click before returning early, and treat a click that didn't actually navigate (e.g., `RequireMenuPath` redirect-to-`/` on insufficient permission) as a miss that should still fall through to the fallback/self-check.

**Chosen approach:** (b).

**Rationale:** The spec's root-cause analysis explicitly notes `RequireMenuPath` will redirect to `/` if permissions are confirmed insufficient — a scenario where the "Katalog" element is clicked but the resulting page is not `/catalog`. Checking the URL after the click (rather than trusting the click alone, as `navigateToTransportBoxReceive` does by checking for content markers instead) is what lets a single shared self-verification step at the end of the function catch every failure mode uniformly, satisfying FR-1 item 3 and NFR-2 without adding a second, UI-path-specific check. This also keeps the function simpler than `navigateToTransportBoxes`, which checks page *content* markers (`h1, h2, h3, [data-testid*="transport"]`) — a URL check is sufficient and simpler here since the acceptance criterion is explicitly `page.url()`-based.

#### Decision 3: Timeout value

**Options considered:** Leave at 2000ms; raise to 5000ms (matching siblings); raise higher (e.g. 10000ms).

**Chosen approach:** 5000ms, matching every other UI-driving helper in the file.

**Rationale:** Spec FR-1 item 2 and NFR-1 both specify 5000ms explicitly, for consistency with existing helpers and to avoid over-lengthening the suite. No reason to deviate.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Single edit to the existing function in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (lines 234-260). No changes to `frontend/test/e2e/catalog/*.spec.ts`, `frontend/src/**`, or any other helper in this file.

### Interfaces and Contracts

Signature unchanged:

```ts
export async function navigateToCatalog(page: any): Promise<void>
```

Updated contract: on return, `page.url()` is guaranteed to contain `/catalog`. If neither the UI path nor the direct-goto fallback reaches `/catalog`, the function throws an `Error` describing what was attempted (UI click path vs. fallback goto) and the final URL — this is new behavior; today the function can return silently while still on the app root.

### Data Flow (target implementation shape)

```
navigateToCatalog(page):
  await navigateToApp(page)

  try:
    if produktySelector.isVisible({ timeout: 5000 }):
      click produktySelector
      await waitForLoadingComplete(page)
      if katalogSelector.isVisible({ timeout: 5000 }):
        click katalogSelector
        await page.waitForLoadState('domcontentloaded')
        await waitForLoadingComplete(page)
        if page.url().includes('/catalog'):
          return   // early return — mirrors sibling helpers' "return on confirmed success"
  catch (e):
    // log only, matching sibling helpers' catch-and-continue style; do not swallow into a silent return
    console.log('UI navigation failed:', e.message)

  // Unconditional fallback — reached on isVisible timeout-miss, on a UI click that
  // didn't land on /catalog, and on a thrown exception alike.
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'
  await page.goto(`${baseUrl}/catalog`)
  await page.waitForLoadState('domcontentloaded')
  await waitForLoadingComplete(page)

  // Self-verification (new) — turns a silent no-op into a fast, diagnosable failure.
  if not page.url().includes('/catalog'):
    throw new Error(`navigateToCatalog: failed to reach /catalog via UI navigation or direct goto fallback; final URL was ${page.url()}`)
```

This is guidance on shape, not literal code to paste — follow the exact style (console.log wording, try/catch structure) already used by `navigateToTransportBoxes` immediately above it in the file for consistency.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| E2E service principal actually lacks `products.catalog.read`, so both UI path and fallback land on `/` via `RequireMenuPath` redirect | Medium | Out of scope for the code fix itself, but the new self-check throw turns this into an immediate, descriptive failure at the helper call site (FR-2) instead of 84 opaque downstream failures — verify principal's permission grant separately per spec FR-2 |
| Raising timeout 2000ms→5000ms adds latency across many test setups | Low | Only affects the "not visible" (worst) case; NFR-1 accepts a few seconds/test since suite runs nightly, not in PR CI |
| Self-check throw changes failure mode for the 9 spec files' own redundant `expect(page.url()).toContain('/catalog')` assertions | Low | Both fail the test either way (throw vs. failed expect); no behavior regression. Removing the now-redundant per-spec assertions is optional cleanup, not required by this fix |

## Specification Amendments

None. The spec's FR-1/FR-2/NFR-1/NFR-2 are implementable as written and match the code read during this review. One clarification worth calling out to the implementer (not a spec change): use a URL check (`page.url().includes('/catalog')`) as the "confirmed success" gate for the early return after the UI click, rather than a content-marker check as in `navigateToTransportBoxes` — see Decision 2 above. This is the simplest way to satisfy the spec's single self-verification requirement without adding a second, path-specific check.

## Prerequisites

None. No migrations, config, or infrastructure changes needed. FR-2 (confirming the E2E principal holds `products.catalog.read` and that the permissions fetch isn't abnormally slow in E2E mode) is an out-of-band verification step, not a code prerequisite — it can be done via a staging smoke run after the code fix lands, per the spec's own framing ("do not silently paper over a slow-permissions-fetch product bug by only raising the E2E helper's timeout").
