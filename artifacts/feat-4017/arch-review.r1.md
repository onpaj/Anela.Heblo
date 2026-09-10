# Architecture Review: Restore `-f` on `git add -A artifacts/feat-{n}`

## Skip Design: true

## Architectural Fit Assessment
This is not a feature — it is a one-line-pattern text restoration inside three prompt/instruction templates (`.claude/agents/orchestrator.md`, `.claude/agents/plan-orchestrator.md`, `.claude/skills/oneshot/SKILL.md`) that are read and executed verbatim by the pipeline's own orchestrator/skill runs. There is no application code, no data model, no API surface, and no UI. "Architectural fit" here reduces to: does the restored text match the known-good baseline these files already had once (PR #3991 / commit `45fb0ef`), and does it avoid touching anything else. It does.

Verified directly in the worktree (`.claude` under the worktree root):
- `orchestrator.md` — 6 occurrences of `git add -A artifacts/feat-{issue_number}` at lines 32, 75, 101, 142, 166, 205, all missing `-f`.
- `plan-orchestrator.md` — 3 occurrences at lines 44, 102, 142, all missing `-f`.
- `oneshot/SKILL.md` — line 150 (`git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`) missing `-f`; line 151 (`git add -A                              # stage code + everything else`) is a separate, unscoped, non-artifacts staging line and is correctly out of scope.
- `git log --oneline -- <these three files>` confirms the sequence: `45fb0ef` (#3980/#3991) added `-f`, then `72784c2` ("remove -f from git add") stripped it back out on 2026-08-30 — exactly the regression described in the brief and spec.
- A repo-wide grep across `.claude/` for `git add -A` turns up no fourth `artifacts/feat-` occurrence anywhere (`implement-orchestrator.md`, `rework-pr/SKILL.md`, `hygiene-pr/resolve_conflict.sh`, and a graphviz `.dot` diagram reference unrelated `git add -A` usage and must not be touched). Total in-scope occurrences: exactly 10, matching brief and spec.

## Proposed Architecture

There is no architecture to propose. This section is intentionally minimal because the fix has zero structural footprint: it changes shell-command text embedded in three Markdown instruction files, restoring one CLI flag at ten specific call sites. No new component, module, interface, or data flow is introduced.

### Component Overview
```
.claude/agents/orchestrator.md        (6x)  git add -A artifacts/feat-{issue_number}
.claude/agents/plan-orchestrator.md   (3x)  git add -A artifacts/feat-{issue_number}
.claude/skills/oneshot/SKILL.md       (1x)  git add -A artifacts/feat-{issue_id}
                                              ^ each becomes: git add -A -f artifacts/feat-{...}
```
These three files are read by the planning/implementing pipeline (`/plan-next-task`, `/implement-next-task`, `/oneshot`) as literal instructions; the fix changes the instruction text only, not any executable code.

### Key Design Decisions

#### Decision 1: Scope of the diff
**Options considered:**
- (a) Restore exactly the `-f` flag at the ten known call sites, matching PR #3991's end state.
- (b) Additionally add a guardrail (lint rule, CI assertion, pre-commit hook) to prevent recurrence.
- (c) Investigate and fix the suspected root cause (`agentharness init` template re-copy reverting local edits).

**Chosen approach:** (a) only, per the spec's explicit scope and NFR-1/NFR-2.

**Rationale:** The brief and spec both frame this as a straight revert-of-a-regression with "no new design decisions." Options (b) and (c) are legitimate follow-up work but are explicitly out of scope in the spec and would inflate a trivial diff into a review-heavy one. Keeping the diff to exactly ten single-flag insertions makes it trivially reviewable and keeps `git diff PR#3991-state..HEAD -- <these files>` at zero for these lines, which is itself the acceptance bar the spec sets (FR-1/FR-2/FR-3, NFR-2).

## Implementation Guidance

### Directory / Module Structure
No new files, directories, or modules. Edit only the three existing files in place:
- `.claude/agents/orchestrator.md`
- `.claude/agents/plan-orchestrator.md`
- `.claude/skills/oneshot/SKILL.md`

### Interfaces and Contracts
The only "contract" is the literal shell command text. At each of the ten call sites, change:
```
git add -A artifacts/feat-{issue_number}
```
(or `{issue_id}` in `oneshot/SKILL.md`) to:
```
git add -A -f artifacts/feat-{issue_number}
```
preserving exact surrounding whitespace/comments (notably the trailing `# ensure all generated .md artifacts are staged` comment on `oneshot/SKILL.md` line 150). Do not touch `oneshot/SKILL.md` line 151 (`git add -A` with no path, no `-f`) — it stages code generally and was never in scope for #3991 or this fix.

### Data Flow
Unaffected structurally; behaviorally, this is the entire point of the fix. `artifacts/feat-{n}/` is `.gitignore`d for normal dev use. `git add -A <path>` silently no-ops on gitignored paths without `-f` — no error, no non-zero exit, nothing that trips the orchestrator's own `git ls-files --error-unmatch` hard-verify step unless that step is both run and acted on. With `-f` restored, the planning/implementing artifacts (`spec.r1.md`, `arch-review.r1.md`, `design.r1.md`, `task-plan.r1.md`, checkpoints) are correctly staged and committed into the feature PR, matching the documented purpose of `artifacts/feat-{n}` as the record of what was planned/decided.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Scope creep — turning a 10-line flag restoration into a broader cleanup, reformat, or guardrail addition | Low | Diff review should show exactly 10 changed lines (single `-f` insertion each), nothing else; reject anything larger at code-review time. |
| Missing one of the ten call sites, or fixing by hardcoded line number that has since shifted | Low | Fix must locate by content match (`git add -A artifacts/feat-{issue_number}` / `{issue_id}`), not line number, per spec FR-1. Post-fix, `grep -rn 'git add -A artifacts/feat-' .claude/` must show zero bare (non `-f`) matches and exactly 10 `-f` matches. |
| **Residual/structural risk — same regression recurs.** The brief's suspected root cause is a cloud session's `agentharness init` bundled template re-copy reverting these three files' local edits at session start, with a later session committing that reverted diff instead of running `git checkout --` to discard it. This specific fix restores the text but does nothing to the mechanism that stripped it — the same pattern already recurred across #3961/#3969/#3975/#3980/#3987/#3989/#3990/#4003/#4017 per `memory/context/state.md`. | **High (recurrence likelihood), but explicitly out of scope for this fix** | Not mitigated here by design (see spec "Out of Scope" and Decision 1). Recommended as a separate follow-up issue: either (a) a CI/lint assertion that these three files contain `-f` at every `artifacts/feat-` `git add -A` site, or (b) fixing `agentharness init`'s template sync to not silently overwrite locally-modified copies of these files, or (c) an explicit orchestrator instruction to `git checkout --` these files before any session-start template sync rather than committing whatever `agentharness init` leaves behind. This review flags but does not resolve that risk. |

## Specification Amendments
None. The spec (`spec.r1.md`) is complete, internally consistent with the brief, and its FR-1 through FR-4 / NFR-1 / NFR-2 acceptance criteria were independently verified against the actual worktree state during this review (exact line numbers, exact occurrence counts, exact `git log` history matching the claimed `45fb0ef` → `72784c2` regression sequence). No changes needed.

## Prerequisites
None. No migrations, config, or infrastructure changes are required. The fix can be implemented immediately as a direct edit to the three named files.
