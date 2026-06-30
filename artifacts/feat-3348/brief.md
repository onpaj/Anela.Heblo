**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **2** catalog failures (text-search-filters).

## Problem
`frontend/test/e2e/catalog/text-search-filters.spec.ts:142/267` — applying a name/code filter is expected to reset to page 1 but stays on page **2** (`frontend/test/e2e/helpers/catalog-test-helpers.ts:325`).

## Inconsistency
Other tests in the same suite explicitly tolerate the "documented pagination reset bug — stays on page 2" (seen passing in the nightly log). The suite contradicts itself.

## Decision required
- If applying a text filter **should** reset to page 1 → fix the app (catalog list page) and keep these tests (keep the `bug` label).
- Otherwise → align these two tests with the documented "stays on page 2" behavior the rest of the suite assumes, remove the misleading "documented bug" comments, and **remove the `bug` label**.

## Acceptance criteria
- Behavior is consistent across the catalog suite.
- The two tests pass and reflect the intended/decided behavior.
