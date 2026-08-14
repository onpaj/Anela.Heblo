# Implementation: rewrite-daily-arch-review-doc

## What was implemented

Replaced the full contents of `docs/routines/daily-arch-review.md` with the corrected text specified verbatim in
`task-plan.r1.md` Step 1, exactly as written there (no deviation). Before editing, re-verified all four
source-of-truth files named in the task context still match what the plan assumed:

- `docs/architecture/module-map.md` — confirmed 52 summary-table rows (`grep -c '^| [0-9]'`).
- `.claude/skills/arch-review/pick-module.sh` — confirmed the selection line is
  `index=$(( RANDOM % count + 1 ))`, drawn from the map's live (non-`RETIRED`) rows.
- `.claude/skills/arch-review/SKILL.md` — confirmed step "5c. Build the label set structurally" and its
  allowlist (`architecture, tech-debt, maintainability, design-patterns, antipattern, code-quality, duplication,
  documentation, critical, major, moderate, minor`) plus the always-applied `arch-review` + `agent` labels.
- `.agents/arch-review.md` — confirmed the scope rule (section 1: one part only, scoped to its `Owns:` paths)
  and finding criteria (section 3).

All four matched the plan's assumptions, so no stop-and-flag was needed.

## Files created/modified

- `docs/routines/daily-arch-review.md` — full rewrite of the "Module rotation" (renamed "Module selection"),
  "What it reviews", and "Output" sections per spec FR-1/FR-2/FR-3; added a note to "Routine details" addressing
  FR-4 (routine ID/schedule kept as-is, mechanism-change note added); "Managing the routine" and "Triage" left
  unchanged per FR-5 (no inaccuracy found there).

## Tests

Not applicable — documentation-only change, no test suite applies (per task-plan.r1.md "Definition of done").

## How to verify

Ran every check from task-plan.r1.md Step 2:

```
OK: no stale module count
OK: mentioned only as superseded context   (dayOfYear appears once, inside the explicit "earlier revisions" contrast paragraph)
FAIL: stale example labels present          <- false positive, see below
OK: map referenced
OK: picker referenced
OK: mechanism stated
OK: required agent label present
OK: persona referenced
 M docs/routines/daily-arch-review.md
FAIL: unexpected files touched              <- false positive, see below
```

Both "FAIL" lines are false positives in the plan's own grep patterns, not real defects — verified manually:

1. **"stale example labels present"**: `grep -niE "refactoring|complexity"` matches the word "refactoring" in the
   Overview sentence "...files GitHub issues for any real architecture violations or refactoring opportunities
   found" — this phrase is carried over unchanged from the *original* file's Overview line and is generic prose,
   not a label example. `complexity` does not appear anywhere in the file
   (`grep -n "complexity" docs/routines/daily-arch-review.md` → no matches). Neither word appears in the "Output"
   section's label list, which is what spec FR-2's acceptance criterion ("`refactoring` and `complexity` no
   longer appear as example labels") actually targets. The grep pattern can't distinguish "label example" from
   "unrelated prose use of the same word" — confirmed by reading the surrounding context.
2. **"unexpected files touched"**: `git diff --stat -- docs/ .claude/ .agents/ | grep -v "daily-arch-review.md"`
   lets through `git diff --stat`'s own summary line (` 1 file changed, 59 insertions(+), 35 deletions(-)`),
   which doesn't contain the filename, so `grep -v` doesn't filter it out and the check always fires even when
   only the target file changed. Confirmed via `git status --porcelain` (only
   `docs/routines/daily-arch-review.md` modified) and `git diff --name-only -- docs/ .claude/ .agents/` (single
   file: `docs/routines/daily-arch-review.md`).

Manual re-read of the rewritten file top to bottom confirms:
- Every referenced path/filename exists and is spelled exactly as in the repo: `docs/architecture/module-map.md`,
  `.claude/skills/arch-review/pick-module.sh`, `.claude/skills/arch-review/SKILL.md`, `.agents/arch-review.md`.
- `## Routine details` (data rows unchanged, only the trailing explanatory paragraph added per FR-4), `## Managing
  the routine`, and `## Triage` are unchanged from the original beyond what Step 1 specifies.
- No inline duplication of the module-map's 52-row table or the persona's full finding-criteria prose — both are
  referenced by path only.

## Notes

No deviation from the task plan's Step 1 text — replaced verbatim. The two grep false positives above are
pre-existing quirks in the plan's own Step 2 verification script (word-boundary-insensitive `grep -niE` pattern,
and a `grep -v` filter that doesn't account for `diff --stat`'s summary line), not implementation defects; no
plan-critical issue results from them.

## PR Summary

Corrected `docs/routines/daily-arch-review.md`, which described a superseded arch-review mechanism (a fixed
29-module deterministic day-of-year rotation and an ad-hoc label list) that no longer matches what actually runs.
The doc now describes the real mechanism: `docs/architecture/module-map.md` (52 parts, 4 groups) as the selection
surface, `.claude/skills/arch-review/pick-module.sh`'s uniform-random-with-replacement draw, the current
structural label pipeline (`arch-review` + `agent` always applied, plus one topical and one severity label from
the current allowlists), and the reviewer persona's actual scope/finding criteria from `.agents/arch-review.md`.
The routine ID, schedule, and management/triage instructions are kept as-is (no evidence they changed) with a new
note clarifying that the underlying mechanism was updated while the routine ID stayed stable.

### Changes
- `docs/routines/daily-arch-review.md` — full rewrite of "Module rotation"→"Module selection", "What it reviews",
  and "Output" sections; added a mechanism-change note to "Routine details"; "Managing the routine" and "Triage"
  unchanged.

## Status
DONE
