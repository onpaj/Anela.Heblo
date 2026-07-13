# Implementation: add-strict-assertions-lenient-transport-specs

## What was implemented

Added one unconditional assertion (`await expect(page.locator('h1')).toContainText('Transportní boxy')`) to the shared `test.beforeEach` block in each of the three transport E2E specs that previously wrapped every meaningful assertion in `if (await x.count() > 0)` guards: `box-items.spec.ts`, `box-workflow.spec.ts`, `ean-integration.spec.ts`. This is FR-4, explicitly marked "recommended, not blocking" in the spec — it lands alongside Tasks 1–3 in this same PR, but its live-staging verification (per NFR-1) happens after deployment, not as part of this pipeline run.

## Files created/modified

- `frontend/test/e2e/transport/box-items.spec.ts` — added strict `h1` assertion to `beforeEach`.
- `frontend/test/e2e/transport/box-workflow.spec.ts` — added strict `h1` assertion to `beforeEach`.
- `frontend/test/e2e/transport/ean-integration.spec.ts` — added strict `h1` assertion to `beforeEach`.

No other lines in any of these three files were touched — all subsequent `if (await x.count() > 0)` conditional logic in the test bodies is unchanged, per the task's explicit scope limit.

## Tests

These are Playwright E2E specs that only run meaningfully against a live staging deploy with a real E2E session (the E2E service-principal identity only exists in Staging/Development). There is no local/mocked equivalent. Static validation performed:
- `npx tsc --noEmit` from `frontend/` — zero new TypeScript errors attributable to any of the three modified files (grepped output for "transport" — no matches).
- `expect` is already imported in all three files (`import { test, expect } from '@playwright/test';`) — no new imports needed.

Full functional verification (confirming the new assertion actually passes against staging) is explicitly deferred per the task spec to after Tasks 1–3 are deployed and confirmed healthy — running these specs before then would fail at the new assertion for the wrong reason (the permission gap Task 1 fixes), which is expected and not a defect in this task's change.

## How to verify

```bash
cd /home/user/worktrees/feature-3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece
./scripts/run-playwright-tests.sh --grep "box-items|box-workflow|ean-integration"
```
(after Tasks 1–3 are deployed to staging)

## Notes

- Per the task's priority note, this is the lowest-priority of the four tasks (recommended hardening, not a blocking fix) — included in this PR because the pipeline delivers the full feature as one branch, but its rollout/verification is sequenced after the primary fix per NFR-1.
- `npm run lint` was not run in isolation for this task since it only covers `src/`, not `test/e2e/` (see task 2's impl notes for the same finding).

## PR Summary

Added a strict, unconditional `h1` assertion to the `beforeEach` of `box-items.spec.ts`, `box-workflow.spec.ts`, and `ean-integration.spec.ts` — the three transport E2E specs that previously "passed" in nightly run #191 despite hitting the identical Transport Box rendering failure as the 4 specs that failed loudly, purely because every assertion in these three files was guarded by `if (await x.count() > 0)`. This closes the silent-pass gap for future regressions on this page.

### Changes
- `frontend/test/e2e/transport/box-items.spec.ts` — added strict `h1` assertion
- `frontend/test/e2e/transport/box-workflow.spec.ts` — added strict `h1` assertion
- `frontend/test/e2e/transport/ean-integration.spec.ts` — added strict `h1` assertion

## Status
DONE
