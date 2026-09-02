# Specification: Fix missing `-f` flag on `git add -A` in `implement-orchestrator.md`

## Summary
`.claude/agents/implement-orchestrator.md` contains several `git add -A` invocations intended to stage pipeline artifacts under `artifacts/feat-{issue_number}/...`, but `artifacts/` is listed in `.gitignore`, so plain `git add -A` silently stages nothing there. This has already caused real pipeline runs to push commits missing their impl/review/code-review artifacts, relying entirely on a downstream `git ls-files --error-unmatch` check to catch it after the push already happened. The fix is a pure flag insertion — `git add -A` → `git add -A -f` — everywhere in this one file where artifacts are being staged, with no other behavioral changes.

## Background
Issue #4017 fixed this exact bug class (`git add -A` failing to stage gitignored `artifacts/` paths) in `orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md`, but explicitly excluded `implement-orchestrator.md` on the stated grounds that "none of those stage `artifacts/feat-{n}`". That exclusion was incorrect: `implement-orchestrator.md` stages `artifacts/feat-{issue_number}/...` in three separate commit steps, and during a live run of `/implement-next-task` for issue #4017 itself, the **Code Review phase** step hit the identical failure — `git add -A` produced a commit containing only an unrelated `state.json` change, `code-review.r1.md` was never staged, and the commit was pushed before the `git ls-files --error-unmatch` hard-verify caught the problem. A prior run had already hit the same bug in the **Handling Review Result** section's developer-task commit step, requiring a manual follow-up commit to land the missing `impl/` and `review/` artifacts. This fix brings `implement-orchestrator.md` in line with the sibling files already patched by #4017.

## Functional Requirements

### FR-1: Add `-f` to the Handling Review Result commit block
In the **Handling Review Result** section, the primary commit-and-verify code block must add `-f` to `git add -A`:

Current text (to be changed):
```bash
git add -A
git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md     # STRICT
git ls-files --error-unmatch artifacts/feat-{issue_number}/review/{task_name}.r{N}.md   # STRICT
git push
```
New text:
```bash
git add -A -f
git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md     # STRICT
git ls-files --error-unmatch artifacts/feat-{issue_number}/review/{task_name}.r{N}.md   # STRICT
git push
```

**Acceptance criteria:**
- The `git add -A` on the line immediately before the `git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true` line reads `git add -A -f`.
- No other line in this code block changes.

### FR-2: Add `-f` to the PASS checkpoint-commit line
In the **Handling Review Result** section, under the **PASS** bullet, the inline checkpoint commit line must add `-f`:

Current: `` `git add -A && git commit -m "chore(feat-{issue_number}): {task_name} passed review" || true && git push` ``
New: `` `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} passed review" || true && git push` ``

**Acceptance criteria:**
- The inline command in the **PASS** bullet reads `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} passed review" || true && git push`.

### FR-3: Add `-f` to the failed-checkpoint commit line
In the **Handling Review Result** section, under the **REVISION_NEEDED** bullet's `N >= max_revisions` sub-case, the inline checkpoint commit line must add `-f`:

Current: `` `git add -A && git commit -m "chore(feat-{issue_number}): {task_name} failed after max revisions" || true && git push` ``
New: `` `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} failed after max revisions" || true && git push` ``

**Acceptance criteria:**
- The inline command in this bullet reads `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} failed after max revisions" || true && git push`.
- The `N < max_revisions` sub-case above it, which currently describes committing the checkpoint update only in prose ("commit and push the checkpoint update") with no explicit `git add -A` shown, is left untouched (no literal `git add -A` string exists there to change).

### FR-4: Add `-f` to the Code Review Fix Pass commit block
In the **Code Review Fix Pass** section, the commit-and-verify code block must add `-f` to `git add -A`:

Current text:
```bash
git add -A
git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/code-review-fixes.r{N}.md   # STRICT
git push
```
New text:
```bash
git add -A -f
git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/code-review-fixes.r{N}.md   # STRICT
git push
```

**Acceptance criteria:**
- The `git add -A` on the line immediately before `git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true` reads `git add -A -f`.
- No other line in this code block changes.

### FR-5: Add `-f` to the Code Review phase commit block
In the **Code Review phase** section, the commit-and-verify code block must add `-f` to `git add -A`:

Current text:
```bash
git add -A
git commit -m "chore(feat-{issue_number}): code review r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/code-review.r{N}.md
git push
```
New text:
```bash
git add -A -f
git commit -m "chore(feat-{issue_number}): code review r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/code-review.r{N}.md
git push
```

**Acceptance criteria:**
- The `git add -A` on the line immediately before `git commit -m "chore(feat-{issue_number}): code review r{N}" || true` reads `git add -A -f`.
- No other line in this code block changes.

### FR-6: Leave the CLEAN / CHANGES_REQUESTED result-handling bullets in Code Review phase untouched
The **Code Review phase** section's step 7 result-handling bullets (CLEAN, CHANGES_REQUESTED with `N < max_revisions`, CHANGES_REQUESTED with `N >= max_revisions`) describe committing and pushing checkpoint/task-context updates in prose ("Commit and push", "commit and push it") and do not contain a literal `git add -A` string.

**Acceptance criteria:**
- No literal `git add -A` string exists in these three bullets in the current file; confirm by content match before editing, and make no edit there if none is found.
- If a literal `git add -A` is found in these bullets at implementation time (i.e., the file has since diverged from the version reviewed for this spec), apply the same `-f` fix there too, consistent with FR-1 through FR-5's rationale.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a documentation/prompt-template text edit with no runtime performance dimension.

### NFR-2: Security
Not applicable — no secrets, auth, or sensitive data are touched. The change only affects which files a local `git add` stages before commit; it does not change repository permissions, CI credentials, or what gets pushed beyond the intended artifact files (which are already produced and written to disk by the orchestrator's own steps, just previously left unstaged).

## Data Model
Not applicable — no data entities are introduced or modified. This is a targeted edit to a single Markdown prompt-template file (`.claude/agents/implement-orchestrator.md`) that is read and executed as instructions by the implementing-stage orchestrator agent.

## API / Interface Design
Not applicable — no API, endpoint, or UI is involved. The "interface" here is the shell command text embedded in the orchestrator's Markdown instructions, which is what an LLM agent executes verbatim when running `/implement-next-task`. The only change is inserting the `-f` flag into five `git add -A` occurrences (three fenced code blocks: Handling Review Result, Code Review Fix Pass, Code Review phase; plus two inline commands within Handling Review Result: the PASS and failed-after-max-revisions checkpoint commits).

## Dependencies
- Depends on the existing `.gitignore` entry for `artifacts/` (unchanged by this fix) — that entry is the reason `-f` is required at all.
- Mirrors the fix already applied by issue #4017 to `orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md`; no code changes to those files are needed or in scope here.
- No external services, libraries, or new tooling are required.

## Out of Scope
- Any change to `orchestrator.md`, `plan-orchestrator.md`, or `oneshot/SKILL.md` — already fixed by #4017.
- Any change to the `git ls-files --error-unmatch` verification logic, the push-ordering behavior (push happens after the verify lines textually but the file does not enforce push being conditional on verify success — that structural concern is noted in the brief but is not part of this fix's scope), or the `.gitignore` file itself.
- Any change to commit messages, checkpoint semantics, revision/retry logic, or any other prose in `implement-orchestrator.md` beyond the `git add -A` → `git add -A -f` substitutions described in FR-1 through FR-5.
- Any change to `agentharness` CLI behavior.
- Retroactively fixing any already-pushed commits that were affected by this bug in past runs (e.g., feat-4017's history) — this spec covers only the template fix going forward.

## Open Questions

None.

## Status: COMPLETE
