# Code Review: add-strict-assertions-lenient-transport-specs

## Summary
The implementation adds exactly the prescribed unconditional `h1` assertion (`await expect(page.locator('h1')).toContainText('Transportní boxy')`) to the `beforeEach` block of all three target files (`box-items.spec.ts`, `box-workflow.spec.ts`, `ean-integration.spec.ts`), verified directly against commit `a252ed4`. The diff stat confirms only 6 insertions per file with zero deletions, meaning no other lines — including the `if (await x.count() > 0)` conditional logic elsewhere in each file — were touched.

## Review Result: PASS

### task: add-strict-assertions-lenient-transport-specs
**Status:** PASS

## Overall Notes
- `git show a252ed4 --stat` shows `3 files changed, 18 insertions(+)` with no deletions — confirms scope discipline (nothing else changed).
- Each file's added block matches the spec's `new_string` verbatim, including the explanatory comment.
- Confirmed `import { test, expect } from '@playwright/test';` is present at the top of all three files, so `expect` is in scope — no missing import, no syntax error.
- This is FR-4, explicitly "recommended, not blocking" per the spec; the impl correctly defers live-staging verification (no local/mocked way to run these specs — E2E service-principal identity only exists in Staging/Development). This is not a deficiency per the review criteria.
- No documentation changes are needed for this task.
