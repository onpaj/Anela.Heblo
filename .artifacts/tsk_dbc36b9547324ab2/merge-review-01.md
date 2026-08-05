# Merge review — PR #3840

**Title:** [arch-review] TestInfrastructure: testing-strategy.md documents a frontend/test layout that doesn't exist and contradicts e2e-module-guide.md
**Base:** `main` · **Head:** `harness/tsk_8dbcfc9d3aae4f23` · **Closes #3838**
**Diff:** 114 files, +4598 / −25 (merge-base `378d56c5`).

## What the PR was supposed to do

Per the task, PR body, and the issue it closes (#3838): a **docs-only** fix to
`docs/architecture/testing-strategy.md` — remove a fabricated `frontend/test/`
directory tree, correct the staging URL to `https`, drop a non-existent VS Code
launch-config section, add a port-scoping note, and align two stray lines. Defer
to `docs/testing/e2e-module-guide.md` instead of restating module layout.

That single-file change is present and is correct:
- `docs/architecture/testing-strategy.md` (+10 / −25) does exactly what plan/design/
  architecture-01.md specified. The narrowed wording ("the runner script has no code
  path that targets 3001/5001") avoids re-asserting a claim that would contradict
  `environments.md`. On its own this file would be an easy approve.

## Why this is a reject — scope

The PR does **far more** than its stated task. The diff against `main` also introduces
an entire unrelated feature and harness working files that have nothing to do with #3838:

- **A whole `test-health` routine** (~3,400 lines, not in `main`):
  - `docs/routines/test-health/test-health-digest.sh` (627 lines), `rp-query.sh`,
    `gh-api.sh` and their `.test.sh` files — executable shell scripts.
  - `docs/routines/test-health/harness/install.sh` + `test-health.agent.json` /
    `test-health.process.json` — a **harness installer** that copies Process/Agent
    configs into `~/harness-root`, references secrets (`RP_API_KEY`, `RP_ENDPOINT`,
    `RP_PROJECT`) and manual GitHub label creation.
  - ~90 fixture JSON files under `docs/routines/test-health/fixtures/`.
  - `docs/superpowers/plans/…` (1,594 lines) and `specs/…` (323 lines).
- **Committed harness artifacts**: `.artifacts/tsk_8dbcfc9d3aae4f23/{plan,design,architecture,development}-01.md`
  — internal working files that should not land in the repo (`.artifacts/` is not
  gitignored, so they would be committed to `main`).

The test-health routine is a separate piece of work (a `feat/test-health-routine`
branch exists); it appears to have been carried onto this branch rather than the
branch being cut from `main`. Whatever the cause, the diff as it stands would merge
an entire executable routine + harness install machinery + working artifacts into
`main` under the cover of a documentation fix.

This is exactly the case the review guidance calls out: *"Unrelated scope is a reason
to withhold, even when the code is good."* Here the unrelated scope is not incidental —
it is ~3,400 of the ~3,800 changed lines and dominates the PR. It is also unreviewable
in the context of this task: a nightly test-health routine with its own design, secrets,
label conventions and harness wiring cannot be responsibly approved as a rider on a
doc-drift ticket, and I have not reviewed it against its own spec.

## Blast radius

- Adds executable shell + a harness installer touching `~/harness-root` and referencing
  secrets — not covered by this repo's CI (no PR-time shell CI), so it would land untested.
- Commits `.artifacts/` working files into the default branch.

## Decision

The intended one-file docs change is fine, but the PR cannot be merged as-is. It must be
reduced to just `docs/architecture/testing-strategy.md` (or the test-health routine split
into its own reviewed PR). Rejecting; a human should re-scope the branch.

```json
{"confidence": 0.03, "reasoning": "The stated task is a one-file docs fix for #3838, but the diff against main bundles an entire unrelated ~3,400-line executable test-health routine, its harness installer, and committed .artifacts working files. Massive scope violation; unreviewable as-is and would pollute main.", "risks": ["Merges an entire unrelated test-health routine (shell scripts, harness installer, ~90 fixtures, specs/plans) into main under a docs-fix PR", "harness/install.sh writes Process/Agent configs into ~/harness-root and references secrets, untested by PR CI", "Commits internal .artifacts/tsk_8dbcfc9d3aae4f23 working files to the default branch"]}
```
