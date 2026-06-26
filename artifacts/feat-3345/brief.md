**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **2** marketing failures (loading "via sidebar", leaflet-generator).

## Problems & root causes
1. **Invalid selector** — `frontend/test/e2e/marketing/loading.spec.ts:15` uses a mixed selector `'a[href="/marketing/calendar"], text="Kalendář"'` → Playwright throws *"Unexpected token = while parsing css selector"*. Replace with a valid locator, e.g. `page.getByRole('link', { name: 'Kalendář' })` or `page.locator('a[href="/marketing/calendar"]')`.
2. **Flaky timeout** — `frontend/test/e2e/marketing/leaflet-generator.spec.ts:36` — `.prose` result not visible within `RESULT_TIMEOUT_MS`; the LLM generation was slow or failed. Confirm the leaflet feature works on staging; if healthy, raise `RESULT_TIMEOUT_MS` to a realistic value; if not, open a follow-up bug for the feature.

## Acceptance criteria
- loading "via sidebar" uses a valid locator and passes.
- leaflet-generator either passes reliably or is documented/tracked as a feature bug.
