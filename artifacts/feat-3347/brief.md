**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **4** core failures (recurring-jobs-management).

## Problem
`frontend/test/e2e/core/recurring-jobs-management.spec.ts` asserts exactly **12** rows/buttons (lines 46, 215, 296, 693); staging returns **24** (exactly 2×).

## Investigate first
Open the recurring-jobs page on staging and count distinct jobs:
- If each job is **rendered twice / duplicated** → real UI/query bug: fix the duplication (keep the `bug` label).
- If there are **genuinely more jobs** → replace the hard-coded `12` with a robust assertion (e.g. `>= N`, or count distinct names) in all four places, and **remove the `bug` label**.

## Acceptance criteria
- Root cause documented in the issue.
- Either the duplication is fixed, or the tests assert the real count without brittleness.
- recurring-jobs specs pass against staging.
