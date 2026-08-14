# Daily Architecture Review Routine

## Overview

A remote Claude Code routine that reviews one part of the codebase's module map per run and files GitHub issues
for any real architecture violations or refactoring opportunities found. Parts are drawn uniformly at random,
with replacement, from `docs/architecture/module-map.md` — a full cycle across all parts takes roughly a month,
but individual parts can be drawn more than once before every part has been drawn at all.

## Routine details

| Field | Value |
|---|---|
| Routine ID | `trig_01TDp4EDif36TJBkrMR2R7v2` |
| Schedule | Daily, every day (`0 23 * * *` UTC = 1am Europe/Prague CEST) |
| Model | `claude-sonnet-4-6` |
| Repo | `https://github.com/onpaj/Anela.Heblo` |
| Environment | Anthropic Cloud (`env_01Ggx2T42z3VtZqdp8k7TSTW`) |
| Web UI | https://claude.ai/code/routines/trig_01TDp4EDif36TJBkrMR2R7v2 |

The routine ID and schedule above have not changed. What changed underneath them is the **mechanism** the routine
invokes each night — see below. Routine configuration itself lives in the Claude Code cloud product, not in this
repository, so this doc cannot assert exactly when or how the prompt changed; what confirms the routine now runs
the mechanism described below is the label set on real filed issues (e.g. #3891), which matches this doc's
"Output" section and not an earlier version of it.

## Module selection

Today's part is **not** chosen by a fixed day-of-year formula. It is drawn by
`.claude/skills/arch-review/pick-module.sh`, which:

1. Reads the live (non-`RETIRED`) rows of `docs/architecture/module-map.md` — currently **52 parts** across four
   groups (business domain, platform/cross-cutting, integration adapters, delivery/tooling). See that file for
   the current list and exact counts; this doc intentionally does not duplicate its rows, since that is exactly
   what went stale last time.
2. Picks one row by **uniform random draw with replacement**: `index = RANDOM % count + 1`.

This means the same part can be drawn again before every other part has been drawn once. Over a full cycle of
runs, expect roughly two-thirds of parts to be touched, unevenly — not a clean non-repeating rotation (per
`.claude/skills/arch-review/SKILL.md`'s own "Selection is uniform random with replacement" note). If the same
part gets reviewed twice in a short span, that is expected behaviour, not a bug.

*(Earlier revisions of this doc described a deterministic `modules[(dayOfYear - 1) % 29]` rotation over a fixed
29-module list. That is not what runs today — a deterministic day-of-year rotation cannot, by construction, ever
redraw a module within a 29-day cycle, which is the opposite of the random-with-replacement behaviour above.)*

## What it reviews

For the one part it draws, the routine follows the reviewer persona at `.agents/arch-review.md`, which:

- Scopes every finding to that part's `Owns:` paths in `docs/architecture/module-map.md`. It does not sweep the
  whole codebase, and a defect in a part this one merely *depends on* is out of scope unless this part itself
  misuses that dependency.
- Grounds findings in the normative documents the map names for that part (or discovers them — `CLAUDE.md`,
  architecture docs, ADRs, contribution guides), verifying documented claims against the code rather than
  trusting a document that may have gone stale.
- Reports: violations of a rule this project has actually documented and still follows; outliers from the
  architecture the rest of the repo follows; misuse of a depended-on technology or framework; best-practice
  errors whose consequence can be stated concretely; duplicated invariants that can drift apart.
- Does **not** report style/formatting preference, missing tests (unless the gap is itself architectural),
  speculative refactors, or anything already covered by an existing `arch-review` issue, open or closed.

See `.agents/arch-review.md` for the full persona — this section summarizes it, it does not replace it.

## Output

Each run produces 0–5 draft issues; **zero is a correct, expected outcome for a clean part.** Every filed issue's
labels are built structurally by `.claude/skills/arch-review/SKILL.md` (step "5c. Build the label set
structurally"), not left to the model's judgement:

- `arch-review` and `agent` — always applied, by the shell command that files the issue, whatever the model
  drafts.
- Exactly one **topical** label from: `architecture, tech-debt, maintainability, design-patterns, antipattern,
  code-quality, duplication, documentation`.
- Exactly one **severity** label from: `critical, major, moderate, minor`.
- Any other label the model attempts to draft is silently dropped, never filed.

Issues include the file path, line range, impact, and a suggested direction — not a written fix. The routine
never makes code changes, never opens PRs, never commits.

## Managing the routine

**Pause/enable/delete:** https://claude.ai/code/routines/trig_01TDp4EDif36TJBkrMR2R7v2

**Trigger a manual run:**
```bash
# Ask Claude Code to run it, or use the web UI
```

**Update the prompt** (e.g. to tune quality bar or which map to review): ask Claude Code — `action: update` on
the routine ID above.

## Triage

Issues filed by this routine appear at:
```
https://github.com/onpaj/Anela.Heblo/issues?q=label%3Aarch-review+is%3Aopen
```

Aim to review and close/resolve them periodically. Issues with no activity after ~90 days are candidates for
closing as "won't fix" or "stale".
