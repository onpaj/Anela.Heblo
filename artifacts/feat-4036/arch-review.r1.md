# Architecture Review: Fix missing `-f` flag on `git add -A` in `implement-orchestrator.md`

## Skip Design: true

This is a five-occurrence flag insertion (`git add -A` → `git add -A -f`) inside one Markdown prompt file, `.claude/agents/implement-orchestrator.md`. There is no application code, no runtime component, no API, no data model, and no UI surface of any kind — the "interface" is literal shell-command text embedded in an LLM agent's instructions. No design work applies.

## Architectural Fit Assessment

This isn't a software architecture change at all — it's a documentation/prompt-content correction to an existing, already-established pattern. The pattern itself (unconditional `git add -A`/`-f` immediately before a commit, followed by a `git ls-files --error-unmatch` hard-verify against the artifact the step is supposed to have produced) is not being introduced here; it already exists in `implement-orchestrator.md`'s three commit blocks and is the exact same shape as the commit steps in `orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md`. The only defect is that `artifacts/` is `.gitignore`d and plain `git add -A` silently no-ops on gitignored paths, so the fix is scoped to inserting `-f` at the five call sites identified by the spec. No new integration points, no new files, no new conventions.

One structural note confirmed by reading the code directly (not part of this fix, but worth stating so it isn't mistaken for missing from scope): `implement-orchestrator.md`'s commit blocks use bare `git add -A` (whole-worktree staging — the file's own prose at the top of **Handling Review Result** explains this is deliberate, to also catch the developer's uncommitted source changes), whereas the sibling files fixed by #4017 use path-scoped `git add -A artifacts/feat-{issue_number}`. I verified directly against `orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md` in this worktree and none of them currently show an `-f` flag anywhere (`grep` for `add -A -f`, `add -Af`, `add -f -A` returns nothing in all three) — so, contrary to the brief's framing, #4017's fix does not appear to be present in this worktree's checkout of those files, and there is no in-repo `-f` placement to mirror. This doesn't block the current fix: the target spelling is fully pinned by `spec.r1.md`'s FR-1 through FR-5 (`git add -A -f`, flag appended directly after `-A`, no other tokens changed), and that is unambiguous and sufficient to implement from. It does mean the developer should not go looking for a "reference" `-f` occurrence elsewhere in the repo and should not attempt to reconcile or fix the sibling files — that is explicitly out of scope here (per the spec's "Out of Scope" section) and belongs to #4017 / its own follow-up, not this issue.

## Proposed Architecture

Not applicable — no components, services, or modules are introduced, removed, or reshaped. This is a text substitution inside one file.

### Component Overview

```
.claude/agents/implement-orchestrator.md   (single file, no other components touched)
  ├─ Handling Review Result section
  │    ├─ fenced bash block:            git add -A        → git add -A -f    (FR-1)
  │    ├─ inline PASS commit line:      git add -A && ...  → git add -A -f && ...  (FR-2)
  │    └─ inline failed-checkpoint line: git add -A && ... → git add -A -f && ...  (FR-3)
  ├─ Code Review Fix Pass section
  │    └─ fenced bash block:            git add -A        → git add -A -f    (FR-4)
  └─ Code Review phase section
       └─ fenced bash block:            git add -A        → git add -A -f    (FR-5)
```

No dependency graph changes; no new files; the `.gitignore` entry for `artifacts/` is unchanged and is in fact the reason `-f` is required.

### Key Design Decisions

#### Decision 1: Flag placement — `git add -A -f` vs. alternatives (e.g. `git add -f -A`, `git add -A --force`, un-ignoring `artifacts/` in `.gitignore`)
**Options considered:**
1. `git add -A -f` (append `-f` after `-A`).
2. `git add -f -A` (flag-before-modifier ordering).
3. `git add -A --force` (long form).
4. Remove `artifacts/` from `.gitignore` entirely, or add a per-directory `!artifacts/feat-*/` negation, so bare `git add -A` would just work.

**Chosen approach:** Option 1, exactly as specified in `spec.r1.md` FR-1–FR-5, matching the flag ordering issue #4017 used for the sibling files (`orchestrator.md`, `plan-orchestrator.md`, `oneshot/SKILL.md` — confirmed by inspecting their commit-block phrasing, even though the `-f` itself is not currently present in this worktree's checkout of those files, per the note above).

**Rationale:** This is a pure bugfix with a spec that already pins the exact before/after text and lists explicit acceptance criteria per occurrence — there is no latitude to exercise here, and introducing any variation (long-form flag, different ordering, or touching `.gitignore`) would both violate the "no other changes" scope discipline this spec and its sibling (#4017) explicitly call for, and would diverge from the established convention. Changing `.gitignore` behavior (option 4) is a materially larger and riskier change — it would affect what every other git command in every other orchestrator template sees under `artifacts/`, not just these five commit sites — and is explicitly out of scope per the spec's "Out of Scope" section.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Edit only `.claude/agents/implement-orchestrator.md` in place.

### Interfaces and Contracts
None in the software sense. The "contract" is the exact text produced by `spec.r1.md`'s FR-1 through FR-5, each with its own before/after block and acceptance criteria — follow those verbatim rather than re-deriving the fix from first principles. In particular:
- FR-1, FR-4, FR-5 each change exactly one line (`git add -A` → `git add -A -f`) inside a fenced bash block, with the surrounding `git commit`/`git ls-files --error-unmatch`/`git push` lines unchanged.
- FR-2 and FR-3 each change one inline backtick-quoted command string (`git add -A && git commit ...` → `git add -A -f && git commit ...`), not a fenced block.
- FR-6 requires confirming (by content match, not by trusting the brief) that no bare `git add -A` string exists in the Code Review phase's CLEAN / CHANGES_REQUESTED prose bullets before making any edit there. I confirmed this directly: those three bullets (lines ~239–265) describe committing in prose ("Commit and push", "commit and push it") with no literal `git add -A` string present — so FR-6 requires no edit, only the confirmation.

I independently verified all five target occurrences by reading the file directly (not by trusting the brief's line numbers, which had drifted slightly from the live file):
- Line 91 — **Handling Review Result** fenced block (FR-1).
- Line 109 — **Handling Review Result**, inline PASS checkpoint commit (FR-2).
- Line 133 — **Handling Review Result**, inline failed-after-max-revisions checkpoint commit (FR-3).
- Line 170 — **Code Review Fix Pass** fenced block (FR-4).
- Line 225 — **Code Review phase** fenced block (FR-5).

A sixth `git add -A` textually appears at line 243 (`... delete it (`git rm -f` or plain `rm` + `git add -A`) so ...`) inside the CLEAN-branch prose — but note this is illustrative prose describing a `git rm`/`git add -A` cleanup of an already-committed, no-longer-gitignored file that was previously staged with `-f` (a tracked file's re-add doesn't need `-f`), not a new artifact-staging occurrence guarded by an `git ls-files --error-unmatch` verification. It does not match the FR-1–FR-6 pattern (no adjacent `git ls-files --error-unmatch` line, and the object being re-added is already tracked, not gitignored) and per the "surgical changes" rule and the spec's explicit scope (FR-1 through FR-6 only), should be left untouched — flagging it here only so the developer doesn't second-guess whether it's a missed occurrence during implementation or code review.

### Data Flow
Not applicable — no runtime data flow. The only "flow" affected is which files `git add -A -f` places in the index before the subsequent `git commit`, which is exactly the artifact files the preceding step wrote (`impl/{task_name}.r{N}.md`, `review/{task_name}.r{N}.md`, `impl/code-review-fixes.r{N}.md`, `code-review.r{N}.md`) plus whatever real source-code changes the developer subagent made in the same working tree.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Edit accidentally touches a `git add -A` occurrence outside FR-1–FR-5 (e.g. the line-243 prose mention) | Low | Match by exact surrounding context (the fenced-block/inline-command text given in the spec), not by a blind find-all-`git add -A`-replace; verify via `grep -c "git add -A -f"` equals 5 and `grep -c "git add -A"` (bare, no `-f`) equals 1 (only the line-243 prose reference) after the edit. |
| Line numbers in the brief/spec have drifted from the live file (already observed: brief cites the Code Review phase block starting near its own re-quote, but actual line numbers are 91/109/133/170/225) | Low | Already mitigated by this review's direct content verification above; developer should match by text content per FR-1–FR-6's acceptance criteria, never by line number. |
| Someone extends this fix to the sibling files (`orchestrator.md`, `plan-orchestrator.md`, `oneshot/SKILL.md`) since they were found to lack `-f` in this worktree | Low | Out of scope per this spec; those files are owned by #4017 (or a fresh follow-up issue if #4017's merge didn't actually land in this branch/worktree) — do not touch them here. |

No risk in this change rises above "low": it is a same-day-revertable text edit to a prompt file with no code path, compile step, or runtime affected until the next time `/implement-next-task` executes the modified section.

## Specification Amendments

None required. `spec.r1.md` is implementation-ready as written — FR-1 through FR-6 fully and correctly pin all five target occurrences plus the one section requiring a no-op confirmation, and I verified all of it directly against the live file. One clarifying note for the implementer (not a spec change, since it doesn't alter any requirement): the brief's claim that sibling files `orchestrator.md`/`plan-orchestrator.md`/`oneshot/SKILL.md` already carry the `#4017` `-f` fix does not hold in this worktree as checked out right now — none of the three contain an `-f` flag on any `git add -A` line. This has no bearing on FR-1–FR-6 (which are self-contained and don't depend on those files), but the implementer should not be surprised by it or attempt to "fix" it as a drive-by.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — the `.gitignore` entry for `artifacts/` that necessitates `-f` already exists and is unchanged by this fix. Implementation can start immediately.
