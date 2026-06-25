# Specification: Fix Invalid CSS Selector and Leaflet Generator Timeout in Marketing E2E Tests

## Summary

Two marketing E2E tests have been failing in the nightly suite (run 28147951139, 2026-06-25). The first fails because of a syntactically invalid Playwright CSS selector that throws at parse time. The second is a flaky timeout caused by an LLM generation call that exceeds the currently configured wait limit. Both must be fixed so the nightly suite passes reliably.

## Background

The nightly E2E suite runs against staging. Two tests in `frontend/test/e2e/marketing/` are consistently red:

1. `loading.spec.ts` — "should navigate to marketing calendar via sidebar": uses the selector string `'a[href="/marketing/calendar"], text="Kalendář"'`, which mixes a CSS attribute selector with a Playwright pseudo-text selector using `=` syntax. Playwright's CSS parser rejects this combination and throws `"Unexpected token = while parsing css selector"`. The test has never passed with this selector.

2. `leaflet-generator.spec.ts` — "generates a leaflet for a known topic": waits up to `RESULT_TIMEOUT_MS` (30 000 ms) for the `.prose` result container to become visible after submitting an LLM generation request. The timeout is being exceeded, causing a flaky failure. Root cause may be that the LLM call legitimately takes longer than 30 s on staging, or that the feature itself is broken.

## Functional Requirements

### FR-1: Fix invalid Playwright selector in loading.spec.ts

Replace line 14 of `frontend/test/e2e/marketing/loading.spec.ts`:

```
const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
```

with a valid Playwright locator. The preferred replacement is:

```
const calendarLink = page.locator('a[href="/marketing/calendar"]').first();
```

An acceptable alternative is `page.getByRole('link', { name: 'Kalendář' })`. Either form must resolve to the same sidebar navigation anchor and must not break any other test in the file. The rest of the test body (visibility assertion, click, `waitForLoadState`, heading assertion) remains unchanged.

**Acceptance criteria:**
- `loading.spec.ts` line 14 no longer contains the mixed-selector string `'a[href="/marketing/calendar"], text="Kalendář"'`.
- The replacement is a valid Playwright locator (CSS attribute selector or `getByRole`).
- Running the `loading.spec.ts` suite on staging produces a pass for "should navigate to marketing calendar via sidebar".
- No other test in `loading.spec.ts` is changed or regresses.

### FR-2: Resolve leaflet-generator timeout

Before changing the test, verify the leaflet feature is functional on staging by manually or automatically confirming that the `/leaflet-generator` page accepts a topic, submits it, and returns a `.prose` result within a reasonable time window.

**If the feature is healthy (responds within ~60 s):**
Raise `RESULT_TIMEOUT_MS` in `frontend/test/e2e/marketing/leaflet-generator.spec.ts` from `30_000` to `90_000` (90 seconds). This covers observed LLM latency spikes without making the test wait indefinitely.

**If the feature is broken (no response, error state, or never returns `.prose`):**
Do not raise the timeout. Instead:
- Leave `RESULT_TIMEOUT_MS` unchanged (or add a code comment explaining why it was not raised).
- Open a GitHub issue describing the staging defect with reproduction steps, and record the issue number in `memory/gotchas/` (a new entry or appended to an existing marketing/leaflet file).
- Mark this FR as partially complete pending the follow-up bug fix.

**Acceptance criteria:**
- Either: `RESULT_TIMEOUT_MS` is raised to `90_000` and the test passes reliably on staging (at least two consecutive nightly runs without a timeout failure).
- Or: A follow-up GitHub issue is filed, its URL is documented in `memory/gotchas/`, and the test file includes a comment referencing the issue number on or near line 36.

## Non-Functional Requirements

### NFR-1: Minimal footprint

Only the two identified lines/constants are changed. No other test files, source files, or configuration is modified as part of this task. If the investigation of FR-2 reveals a staging defect in the leaflet feature itself, that defect is tracked as a separate follow-up issue — it is not fixed within this task.

### NFR-2: Test stability

The fixed selector in FR-1 must be deterministic (no text-match ambiguity). Prefer the href-based CSS locator `a[href="/marketing/calendar"]` because it is structurally tied to the route and immune to translation changes. Use `getByRole` only if the href selector matches more than one element on the page.

## Data Model

No data model changes. This task is limited to E2E test source files.

## API / Interface Design

No API changes. The only artefacts produced are:

- Modified: `frontend/test/e2e/marketing/loading.spec.ts` (line 14)
- Modified or annotated: `frontend/test/e2e/marketing/leaflet-generator.spec.ts` (constant `RESULT_TIMEOUT_MS` on line 7, and/or a comment near line 36)
- Possibly created: a `memory/gotchas/` entry if the leaflet feature is found to be broken

## Dependencies

- Access to staging environment to verify leaflet feature health (FR-2 investigation).
- `./scripts/run-playwright-tests.sh` must be runnable against staging to confirm FR-1 passes.
- No new npm packages or backend changes required.

## Out of Scope

- Fixing any underlying defect in the leaflet generation feature itself (backend LLM pipeline, RAG, knowledge base). That is a separate follow-up.
- Changing any other E2E test file outside `frontend/test/e2e/marketing/`.
- Refactoring the `navigateToApp` or `navigateToMarketingCalendar` helpers.
- Adding new test cases.

## Open Questions

None.

## Status: COMPLETE
