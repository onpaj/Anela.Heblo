**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **3** issued-invoices failures (filters).

## Problem (`frontend/test/e2e/issued-invoices/filters.spec.ts`)
- `:70` invoice-ID filter → empty-state `text="Žádné faktury nebyly nalezeny."` not visible (copy changed, or results existed).
- `:177` "Show Only Unsynced" and `:202` "Show Only With Errors" → `locator.check()` times out 30 s (checkbox not found / not interactable).

## Investigate
Open issued-invoices on staging; verify the empty-state copy and the two checkbox locators (confirm they aren't disabled or relabeled). Update selectors accordingly.

## Acceptance criteria
- The three issued-invoices filter tests locate the current controls and pass.
