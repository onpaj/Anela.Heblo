# Specification: Restore `-f` flag on `git add -A artifacts/feat-{n}` in orchestrator/skill templates

## Summary
Commit `72784c2` regressed the fix from #3980/PR #3991 by stripping the `-f` (force) flag from ten `git add -A artifacts/feat-{n}` invocations across `.claude/agents/orchestrator.md`, `.claude/agents/plan-orchestrator.md`, and `.claude/skills/oneshot/SKILL.md`. Because `artifacts/` is `.gitignore`d, every one of these commands currently stages nothing, so planning/implementation artifacts (`spec.r1.md`, `arch-review.r1.md`, `design.r1.md`, `task-plan.r1.md`, etc.) silently fail to land in PRs. This spec restores `-f` at all ten call sites and verifies no other occurrence is missed.

## Background
`artifacts/feat-{n}/` holds the working documents (spec, architecture review, design doc, task plan, checkpoints) that the planning and implementing orchestrators produce as they run. These directories are intentionally `.gitignore`d in normal development so that scratch state doesn't pollute unrelated commits — but the orchestrator pipeline is a special case: it *must* commit its own artifacts into the feature PR so the record of what was planned/decided ships with the code. `git add -A <path>` respects `.gitignore` and silently no-ops on ignored paths unless `-f`/`--force` is passed; there is no error, no non-zero exit code, nothing that trips the orchestrator's own "hard-verify with `git ls-files --error-unmatch`" safety check *unless that check is actually run and its failure is actually acted upon*. Issue #3980 / PR #3991 already fixed this by adding `-f` everywhere the pipeline stages `artifacts/feat-{n}`. Commit `72784c2` ("chore: update orchestrator and oneshot skill templates (remove -f from git add)", 2026-08-30) removed it again across all three affected files, falsely attributing the change to "Applied by session-start hook" (the actual `scripts/cloud-session-setup.sh` hook does not touch these files). Per `memory/context/state.md`, this same regression pattern has recurred across issues #3961/#3969/#3975/#3980/#3987/#3989/#3990/#4003 — most likely because a cloud session's `agentharness init` bundled template reverts the local working-tree copy of these files at session start, and a later session commits that reverted diff instead of discarding it with `git checkout --`. This bugfix is a straight revert-of-a-regression: restore exactly the state PR #3991 established, with no new design decisions.

## Functional Requirements

### FR-1: Restore `-f` on all `git add -A artifacts/feat-{issue_number}` lines in `orchestrator.md`
In `.claude/agents/orchestrator.md`, change every occurrence of the literal line
```
git add -A artifacts/feat-{issue_number}
```
to
```
git add -A -f artifacts/feat-{issue_number}
```
This occurs at six locations: lines 32, 75, 101, 142, 166, and 205 (line numbers as of the current worktree state; the fix must locate and update by content match, not hardcoded line number, since the file may have shifted slightly by the time this is implemented).

**Acceptance criteria:**
- `grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/orchestrator.md` returns exactly `6`.
- `grep -c 'git add -A artifacts/feat-{issue_number}$'` (i.e., the line ending immediately after the path, with no `-f`) returns `0` for this file — no unfixed occurrence remains.
- No other line in the file is modified (diff shows only the six `-f` insertions).

### FR-2: Restore `-f` on all `git add -A artifacts/feat-{issue_number}` lines in `plan-orchestrator.md`
In `.claude/agents/plan-orchestrator.md`, apply the identical change at three locations: lines 44, 102, and 142.

**Acceptance criteria:**
- `grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/plan-orchestrator.md` returns exactly `3`.
- No occurrence of the unfixed form (`git add -A artifacts/feat-{issue_number}` without `-f`) remains in the file.
- No other line in the file is modified.

### FR-3: Restore `-f` on the `git add -A artifacts/feat-{issue_id}` line in `oneshot/SKILL.md`
In `.claude/skills/oneshot/SKILL.md`, line 150 currently reads:
```
git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```
Change it to:
```
git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```
Note: line 151 (`git add -A                              # stage code + everything else`) is a separate, unscoped `git add -A` for staging code changes generally — it is not artifacts-specific, was not touched by PR #3991, and must **not** be modified by this fix.

**Acceptance criteria:**
- Line 150 (identified by content, not position) reads exactly `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged` (trailing comment text and spacing preserved verbatim from the original).
- Line 151's separate `git add -A` (the code-staging line) is unchanged — still lacks `-f` and still lacks any path argument.
- No other line in the file is modified.

### FR-4: Full-repo sweep confirms no other regressed occurrence
Before closing this fix out, re-run a repo-wide search for `git add -A artifacts/feat-` across all tracked files (not just the three named files) to confirm the brief's enumeration is complete and no fourth location was missed by commit `72784c2` or introduced since.

**Acceptance criteria:**
- `grep -rn 'git add -A artifacts/feat-' .claude/` (or equivalent recursive search across the repo) after the fix shows every match containing `-f` (i.e., matches `git add -A -f artifacts/feat-`) — zero matches of the bare `git add -A artifacts/feat-` form remain anywhere in the repo.
- Total occurrences after fix: 10 (6 + 3 + 1), matching the brief's count exactly. If the sweep finds a different count, treat that discrepancy as a blocker and surface it rather than silently adjusting scope.

## Non-Functional Requirements

### NFR-1: Change scope discipline
This is a targeted revert of a specific regression, not a broader cleanup pass. Only the ten `-f`-flag insertions (and nothing else) should appear in the diff. Do not reformat surrounding prose, renumber steps, or touch the unrelated `git add -A` lines in `implement-orchestrator.md`, `writing-plans/SKILL.md`, `backmerge-prs/`, `rework-pr/SKILL.md`, or line 151 of `oneshot/SKILL.md` — none of those stage `artifacts/feat-{n}` and are out of scope.

### NFR-2: No behavioral ambiguity
The fix must not introduce any conditional logic, comments explaining "why `-f` is needed" beyond what already exists, or restructuring of the surrounding steps. It is a pure flag restoration matching the exact state PR #3991 established, so that future diffs against that PR show zero delta on these lines.

## Data Model
Not applicable — this is a documentation/prompt-template fix with no runtime data model impact. The only "data" involved is the git-tracked Markdown template files themselves.

## API / Interface Design
Not applicable — no API or UI surface is touched. The "interface" here is the literal shell command text embedded in the three agent/skill instruction files, which downstream orchestrator/skill executions (human or agent) read and execute verbatim.

## Dependencies
- Depends on the prior fix in PR #3991 (issue #3980) as the reference baseline to restore.
- No new external libraries, services, or schema changes.
- Downstream consumers: `/plan-next-task` and `/implement-next-task` pipeline runs, and any session invoking `orchestrator.md`, `plan-orchestrator.md`, or the `oneshot` skill, depend on this fix landing for their planning/implementation artifacts to actually commit into PRs.

## Out of Scope
- Diagnosing or fixing the root cause of the recurring regression itself (the suspected `agentharness init` template-revert-at-session-start mechanism named in the brief). That is a harness/tooling issue tracked separately (per `memory/context/state.md` references to #3961/#3969/#3975/#3980/#3987/#3989/#3990/#4003) and is not part of this bugfix.
- Any process or guardrail change to prevent future regressions of this same flag (e.g., a lint rule, a pre-commit check, or CI assertion that these files contain `-f`). If desired, that would be a separate follow-up issue.
- Modifying `implement-orchestrator.md` or any other file whose `git add -A` calls do not target `artifacts/feat-{n}`.
- Modifying the unscoped `git add -A` on line 151 of `oneshot/SKILL.md`.

## Open Questions
None.

## Status: COMPLETE
