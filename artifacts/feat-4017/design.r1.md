# Design: Restore `-f` flag on `git add -A artifacts/feat-{n}`

## Component Design

This change has no application components in the usual sense — the "components" are three Markdown instruction templates that the orchestrator/skill pipeline reads and executes verbatim. Each is edited in place; none change structure, only the literal shell-command text at specific call sites. No new files, modules, or interfaces are introduced.

### `.claude/agents/orchestrator.md`
Responsibility: instructs the implementing-orchestrator pipeline run to stage the `artifacts/feat-{issue_number}` working-document directory before committing.

Six occurrences of the literal line
```
git add -A artifacts/feat-{issue_number}
```
at lines 32, 75, 101, 142, 166, and 205 must each become
```
git add -A -f artifacts/feat-{issue_number}
```
Line numbers are as observed in the current worktree; the fix must locate each occurrence by exact content match (not by hardcoded line number), since the file may have shifted slightly between review and implementation.

### `.claude/agents/plan-orchestrator.md`
Responsibility: instructs the planning-orchestrator pipeline run to stage the same `artifacts/feat-{issue_number}` directory before committing planning artifacts (spec, arch-review, design, task-plan).

Three occurrences of the identical literal line
```
git add -A artifacts/feat-{issue_number}
```
at lines 44, 102, and 142 must each become
```
git add -A -f artifacts/feat-{issue_number}
```

### `.claude/skills/oneshot/SKILL.md`
Responsibility: instructs the oneshot pipeline entry point to stage generated artifacts, then separately stage code and everything else.

Line 150 currently reads
```
git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```
and must become
```
git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```
with the trailing comment and spacing preserved verbatim.

Line 151 —
```
git add -A                              # stage code + everything else
```
— is a separate, unscoped `git add -A` with no path argument. It stages code changes generally, was not part of PR #3991's fix, and must **not** be modified by this change.

### Nature of the fix
Across all three files, the fix is purely textual: insert the literal substring ` -f` (a single space, then `-f`) immediately after `-A` and before the `artifacts/feat-{...}` path argument, at exactly the ten call sites enumerated above. No conditional logic, no new comments, no reformatting of surrounding prose or step numbering, and no changes to any other `git add -A` invocation in these or any other file (e.g. `implement-orchestrator.md`, `writing-plans/SKILL.md`, `backmerge-prs/`, `rework-pr/SKILL.md`, or `oneshot/SKILL.md` line 151 are all out of scope and must show zero diff). The end state must match exactly what PR #3991 (commit `45fb0ef`) previously established, before it was regressed by commit `72784c2`.

A repo-wide sweep for `git add -A artifacts/feat-` across all tracked files (not just these three) confirms after the fix that every match contains `-f` and no bare (unfixed) form remains anywhere in the repo — total occurrences: 10 (6 + 3 + 1).

## Data Schemas

Not applicable. This is a documentation/prompt-template text fix with no database schema, API request/response shape, or event payload involved. The only "data" touched is the git-tracked Markdown template files themselves, and the only runtime effect is which shell command a pipeline run executes.
