# Merge review — PR #3849

**Title:** [arch-review] TransportBoxes: state-machine validation reasons collapse to a generic ValidationError and are dropped by the UI
**Closes:** #3845
**Base:** `main` (`0a5ebe8b`) · **Head:** `harness/tsk_4611b0ae33ca4e05` (`61a5774b`) · merge-base `664cbdde`
**Diff:** 132 files, +5590 / −14

## Verdict: REJECT

The intended TransportBox fix is small and looks well done. But the PR's diff against `main`
carries **~110 files of entirely unrelated content** that would be merged into the default
branch. That unrelated scope alone is disqualifying for an unattended merge, regardless of the
quality of the in-scope change.

## The in-scope change (good, ~14 files)

The actual fix for #3845 is sound and matches its description:

- `TransportBoxExceptions.cs` (new): 4 typed domain exceptions subclassing `ValidationException`.
- `ErrorCodes.cs`: 4 new codes 1406–1409 with correct `[HttpStatusCode]` attributes
  (BadRequest / BadRequest / UnprocessableEntity / UnprocessableEntity).
- Per-handler `catch` clauses across ChangeTransportBoxState / OpenOrResumeBoxByCode /
  RemoveItemFromBox / AddItemToBox handlers, plus `TransportBox.cs`.
- Frontend: generated `api-client.ts` enum updated with the 4 codes, Czech `i18n.ts` templates
  (single-brace `{code}` / `{currentState}` / `{allowedStates}` placeholders), and
  `errorHandler.test.ts` cases.
- Backend tests added/updated for domain + 4 handlers.

The two prior review rounds caught and fixed the real bug here (the generated FE enum not being
regenerated). Taken in isolation, this part would be mergeable. (Minor, non-blocking: enum member
is `TransportBoxCodeInvalidFormat` while the exception type is `TransportBoxCodeFormatException` —
a naming inconsistency, not a defect, assuming the handler maps them correctly.)

## Why this is a reject — unrelated scope with real blast radius

Confirmed via `git merge-base --is-ancestor` that the following are NOT on `origin/main` and are
introduced by merging this PR (three-dot diff = what GitHub merges):

1. **An entire unrelated tooling tree — `docs/routines/test-health/`** (~103 files):
   - Executable bash: `test-health-digest.sh` (627 lines), `install.sh`, `gh-api.sh`,
     `rp-query.sh`, plus `.test.sh` files.
   - Harness config JSON: `harness/test-health.agent.json`, `test-health.process.json`.
   - **97 ReportPortal API fixture JSON dumps** under `fixtures/`.
   - Specs/plans: `docs/superpowers/plans/2026-08-02-test-health-routine.md` (1594 lines),
     `docs/superpowers/specs/2026-08-02-test-health-routine-design.md`.
   This is a whole separate feature/routine (with installer + harness wiring) that has nothing to
   do with #3845. It carries its own blast radius (an installer script and agent/process configs)
   and has not been reviewed as part of this task.

2. **Internal agent-workflow artifacts committed into the product repo** (~7 files):
   `.artifacts/tsk_4611b0ae33ca4e05/{plan,design,architecture,development-01,development-02,review-01}.md`
   and `frontend/.artifacts/tsk_4611b0ae33ca4e05/review-02.md`. These are pipeline scratch/output
   for a *different* task id (`tsk_4611b0ae33ca4e05`, this branch) and should never land in `main`.

These commits (`c5401512`…`33105809`, `96b66512`, etc.) are part of this branch's own history,
not brought in by the `[land] merge main`. Merging PR #3849 pollutes `main` with all of it.

The review contract is explicit: "Unrelated scope is a reason to withhold, even when the code is
good." A reviewer would send this back to be split — the TransportBox fix on its own branch, and
the test-health routine (if wanted) as its own reviewed PR — before merging.

## What I verified / did not

- Read the PR body/issue reference, the full changed-file list, the core diff (exceptions,
  ErrorCodes, api-client enum, i18n), and confirmed the unrelated files are genuinely introduced
  by this merge (not a stale-base artifact) via `merge-base --is-ancestor` and `ls-tree` on
  `origin/main`.
- I did NOT run the build/test suite: the scope defect is disqualifying on its own, so no test
  result could move this to approve. Prior rounds report backend 273/273 + FE build/lint green;
  that speaks to the in-scope code, not the bundled unrelated content.

```json
{"confidence": 0.02, "reasoning": "The intended TransportBox fix is small and clean, but the PR's merge diff introduces ~110 unrelated files into main — an entire test-health routine (executable installer/scripts, harness configs, 97 fixture JSONs, 1900+ lines of specs/plans) plus internal agent-workflow artifacts. Unrelated scope with its own blast radius is disqualifying for an unattended merge.", "risks": ["Merges an entire unreviewed test-health tooling tree (installer + agent/process harness configs) into main", "Commits internal .artifacts/ pipeline scratch for a different task id into the product repo", "132-file / +5590-line diff is far larger than the #3845 fix and cannot be safely merged unattended", "test-health bash scripts and JSON configs carry execution/install blast radius outside this task's scope"]}
```
