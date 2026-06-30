# Architecture Review: Fix Invalid CSS Selector and Leaflet Generator Timeout in Marketing E2E Tests

## Skip Design: true

## Architectural Fit Assessment

Both fixes are surgical test-only changes. No production code, API contracts, or data models are touched. The changes fit entirely within the existing E2E test layer at `frontend/test/e2e/marketing/`.

The project's E2E suite follows a consistent locator style — `page.locator('button').filter({ hasText: '...' })`, `page.getByRole(...)`, and `page.locator('text="..."')` — documented in `memory/patterns/e2e-test-structure.md`. The broken selector in `loading.spec.ts` is the only instance of the mixed CSS+text pseudo-syntax in the entire suite; every other test uses the established patterns.

A directly relevant precedent exists: `navigateToMarketingCalendar()` in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (line 424) already locates the same "Kalendář" sidebar link using `page.locator('text="Kalendář"').first()`. The fix for FR-1 should mirror that exact approach.

For FR-2, `leaflet-generator.spec.ts` is structurally isolated — it is the only test in the marketing folder that exercises an LLM-backed endpoint. No shared constants or helper abstractions exist for LLM timeouts; `RESULT_TIMEOUT_MS` is local to that file. The pattern used elsewhere for slow operations is simply a larger inline `timeout` value (e.g., auth retries up to 120 000 ms in `e2e-auth-helper.ts`).

A parallel leaflet test suite exists at `frontend/test/e2e/leaflet-generator/leaflet-doc-management.spec.ts`. It avoids the generation path entirely — it only tests the document management tab — so it is unaffected.

## Proposed Architecture

### Component Overview

```
frontend/test/e2e/marketing/
  loading.spec.ts          ← FR-1: change line 14 only
  leaflet-generator.spec.ts ← FR-2: raise RESULT_TIMEOUT_MS constant

memory/gotchas/            ← FR-2 (if feature broken): new entry
```

No new files, no new abstractions, no shared constants to introduce.

### Key Design Decisions

#### Decision 1: Locator replacement strategy for loading.spec.ts

**Options considered:**
- `page.locator('a[href="/marketing/calendar"]')` — valid CSS selector, targets by URL
- `page.getByRole('link', { name: 'Kalendář' })` — semantic ARIA role locator
- `page.locator('text="Kalendář"').first()` — text-content locator, matches the helper

**Chosen approach:** `page.locator('text="Kalendář"').first()`

**Rationale:** This is what `navigateToMarketingCalendar()` in `e2e-auth-helper.ts` (line 424) already uses for the identical element. Consistency with the established helper is more important than selecting by href. The href-based selector is brittle if routes change; a role-based selector risks matching the toolbar "Kalendář" toggle button that appears on the calendar page itself (it is also rendered as an interactive element with that label). The text locator scoped with `.first()` is what the suite already validates in production runs via the helper.

#### Decision 2: Timeout value for leaflet-generator.spec.ts

**Options considered:**
- Raise `RESULT_TIMEOUT_MS` to 60 000 ms — conservative doubling
- Raise `RESULT_TIMEOUT_MS` to 90 000 ms — as specified by FR-2
- Annotate and file a bug without raising — only if feature is broken on staging

**Chosen approach:** Raise to `90_000` after confirming the feature is healthy on staging; file a bug and annotate if broken.

**Rationale:** The comment on line 34 already says "LLM call can take up to 25 s". Staging performance is known to be slower than local. 90 s (3× the original limit) gives sufficient headroom while remaining within the nightly suite's practical time budget. No evidence exists that 30 s was ever measured — it appears to have been an arbitrary initial guess.

## Implementation Guidance

### Directory / Module Structure

No new directories or files are required unless the leaflet feature is found to be broken on staging, in which case a single file is added:

```
memory/gotchas/leaflet-generator-llm-timeout.md   ← only if feature is broken
```

### Interfaces and Contracts

No interfaces or API contracts change.

The only two touch-points:

**File 1:** `frontend/test/e2e/marketing/loading.spec.ts`, line 14

Replace:
```typescript
const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
```
With:
```typescript
const calendarLink = page.locator('text="Kalendář"').first();
```

No other lines in this file change. The subsequent `await expect(calendarLink).toBeVisible(...)` and `await calendarLink.click()` calls on lines 15–16 remain identical.

**File 2:** `frontend/test/e2e/marketing/leaflet-generator.spec.ts`, line 7

Replace:
```typescript
const RESULT_TIMEOUT_MS = 30_000;
```
With:
```typescript
const RESULT_TIMEOUT_MS = 90_000;
```

If the feature is broken (staging produces an error state rather than a `.prose` container), additionally add a comment above line 36:
```typescript
// TODO: Leaflet generation is broken on staging — tracked in GitHub issue #XXXX.
// This assertion will time out until the feature is restored.
```

### Data Flow

FR-1 is a locator-only change; the test flow is unchanged:

```
navigateToApp(page)
  → expand Marketing section (line 9)
  → page.locator('text="Kalendář"').first()   ← fixed line 14
  → .click()
  → assert h1 "Marketingový kalendář"
```

FR-2 is a constant-only change; the test flow is unchanged:

```
navigateToApp(page)
  → page.goto('/leaflet-generator')
  → fill topic, select options, click Vygenerovat leták
  → await .prose visible (timeout: 90_000)    ← raised constant
  → validate content length
  → copy button interaction
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `text="Kalendář"` matches the wrong element (e.g., toolbar toggle button) if sidebar and page content load together | Low | The `.first()` guard and the `toBeVisible({ timeout: 5000 })` gate mean the locator resolves against whichever element appears first; since this test starts from the home page before the calendar page is loaded, the sidebar link will appear before the toolbar button. If this causes a flake in future, narrow to `page.locator('nav').locator('text="Kalendář"')` or use the href selector. |
| 90 s timeout causes the nightly suite to exceed its wall-clock budget if the LLM is reliably slow | Low | The suite runs nightly and already has 120 s retries for auth. One test at 90 s is acceptable. The constant is named and easy to adjust. |
| Feature is broken on staging and filing a bug conflates the test fix with the feature fix | Medium | Keep them separate: raise the timeout and file the feature bug independently. The test should not be skipped — it is the canary for the feature. Only add a comment if staging reliably errors (HTTP 5xx or empty response), not if it's just slow. |

## Specification Amendments

The spec is accurate and complete. One clarification to add to FR-2 for the implementer:

**Amendment to FR-2:** Before raising the timeout, perform a manual smoke test of the leaflet generation feature on staging:
1. Navigate to `/leaflet-generator` on `https://heblo.stg.anela.cz`
2. Fill in a topic ("Bisabolol pro citlivou pleť"), select defaults, click "Vygenerovat leták"
3. Observe whether a `.prose` container appears within ~60 s

If the container appears: raise `RESULT_TIMEOUT_MS` to `90_000` and the fix is complete.

If the container does not appear, or an error state is shown: file a GitHub issue via `gh issue create`, document in `memory/gotchas/`, add a comment near line 36, and leave `RESULT_TIMEOUT_MS` unchanged (raising it would not fix a broken feature, only delay the failure).

## Prerequisites

No migrations, infrastructure changes, or configuration changes are required. The only prerequisite for FR-2 is manual access to the staging environment (`https://heblo.stg.anela.cz`) to verify feature health before deciding between the two resolution paths.
