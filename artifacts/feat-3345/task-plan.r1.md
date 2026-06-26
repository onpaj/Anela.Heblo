# Fix Invalid CSS Selector and Leaflet Generator Timeout in Marketing E2E Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two failing nightly E2E tests in the marketing module — one caused by an invalid Playwright CSS selector, and one caused by a timeout too short for an LLM generation call.

**Architecture:** Both fixes are confined to E2E test files only — no production code changes. The selector fix replaces a mixed/invalid CSS+text selector syntax with a plain text selector. The timeout fix raises the wait constant after confirming the staging feature is healthy; if staging is broken a bug is filed instead.

**Tech Stack:** Playwright (TypeScript), `gh` CLI for issue filing if needed.

---

## File Structure

| File | Action | Purpose |
|------|--------|---------|
| `frontend/test/e2e/marketing/loading.spec.ts` | Modify line 14 | Replace invalid mixed selector with `text="Kalendář"` |
| `frontend/test/e2e/marketing/leaflet-generator.spec.ts` | Modify line 7 | Raise `RESULT_TIMEOUT_MS` from `30_000` to `90_000` (or annotate if staging broken) |

---

### task: fix-invalid-css-selector

#### Fix Invalid CSS Selector in loading.spec.ts

**Files:**
- Modify: `frontend/test/e2e/marketing/loading.spec.ts`

**Context:**  
Line 14 currently reads:
```ts
const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
```
The comma-separated `a[href="..."], text="..."` syntax is not valid in Playwright — it is not a CSS list selector; Playwright would interpret the whole string as a single broken selector. The `navigateToMarketingCalendar()` helper in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (line 424) uses `page.locator('text="Kalendář"')` to find the same sidebar link, which is the confirmed working pattern.

- [ ] **Step 1: Open and read the current file**  
  Read `frontend/test/e2e/marketing/loading.spec.ts` and confirm line 14 still contains `'a[href="/marketing/calendar"], text="Kalendář"'`.

- [ ] **Step 2: Replace the broken selector**  
  On line 14, change:
  ```ts
  const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
  ```
  to:
  ```ts
  const calendarLink = page.locator('text="Kalendář"').first();
  ```
  This matches the exact pattern used by the `navigateToMarketingCalendar()` helper and is the minimal change needed.

- [ ] **Step 3: Verify only line 14 changed**  
  Run `git diff frontend/test/e2e/marketing/loading.spec.ts` and confirm exactly one line was modified — the `calendarLink` locator. No other lines should differ.

- [ ] **Step 4: Commit**  
  ```
  git add frontend/test/e2e/marketing/loading.spec.ts
  git commit -m "fix(e2e): replace invalid mixed CSS selector in loading.spec.ts"
  ```

---

### task: fix-leaflet-generator-timeout

#### Raise RESULT_TIMEOUT_MS in leaflet-generator.spec.ts

**Files:**
- Modify: `frontend/test/e2e/marketing/leaflet-generator.spec.ts`

**Context:**  
Line 7 defines `RESULT_TIMEOUT_MS = 30_000`. The test waits at line 36 for a `.prose` container to appear after clicking "Vygenerovat leták". The LLM call behind this can take longer than 30 s. The arch review confirms the timeout should be raised to `90_000` provided the leaflet-generator feature is healthy on staging.

Staging URL pattern: check `docs/architecture/environments.md` for the staging base URL if you need it. The feature lives at the `/leaflet-generator` path.

- [ ] **Step 1: Verify staging is reachable**  
  Open the staging app at the `/leaflet-generator` path (URL from `docs/architecture/environments.md`). Confirm the page loads, the "Generátor letáků" heading is visible, and filling in a topic + clicking "Vygenerovat leták" eventually produces a result (wait up to ~90 s manually). If the page returns an error, a 404, or the generation step throws, **do not change the timeout** — proceed to the bug-filing path in step 4.

- [ ] **Step 2 (healthy path): Raise the timeout**  
  If staging is healthy, on line 7 change:
  ```ts
  const RESULT_TIMEOUT_MS = 30_000;
  ```
  to:
  ```ts
  const RESULT_TIMEOUT_MS = 90_000;
  ```
  No other lines should be touched.

- [ ] **Step 3 (healthy path): Verify only line 7 changed**  
  Run `git diff frontend/test/e2e/marketing/leaflet-generator.spec.ts` and confirm exactly one line was modified.

- [ ] **Step 4 (broken staging path): File a GitHub issue and annotate**  
  If staging is broken (feature errors out or page is missing), do the following instead:

  a. File a GitHub issue using the `gh` CLI:
  ```bash
  gh issue create \
    --title "Leaflet generator broken on staging — E2E timeout cannot be resolved" \
    --body "The leaflet-generator feature at /leaflet-generator on staging is not functioning. The E2E test in frontend/test/e2e/marketing/leaflet-generator.spec.ts has RESULT_TIMEOUT_MS=30_000 which causes a timeout, but raising the value cannot fix an underlying feature breakage. Needs investigation." \
    --label bug
  ```
  Note the issue number from the output (e.g. `#123`).

  b. Add a comment on the line directly above `RESULT_TIMEOUT_MS` in `leaflet-generator.spec.ts`:
  ```ts
  // TODO: raise to 90_000 once leaflet-generator is fixed on staging — see issue #<number>
  const RESULT_TIMEOUT_MS = 30_000;
  ```

- [ ] **Step 5: Commit**  
  **Healthy path:**
  ```
  git add frontend/test/e2e/marketing/leaflet-generator.spec.ts
  git commit -m "fix(e2e): raise RESULT_TIMEOUT_MS to 90_000 for LLM generation wait"
  ```
  **Broken staging path:**
  ```
  git add frontend/test/e2e/marketing/leaflet-generator.spec.ts
  git commit -m "chore(e2e): annotate leaflet-generator timeout with follow-up issue #<number>"
  ```
