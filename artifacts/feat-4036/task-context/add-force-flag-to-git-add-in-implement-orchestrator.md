### task: add-force-flag-to-git-add-in-implement-orchestrator

**Goal:** In `.claude/agents/implement-orchestrator.md`, change every `git add -A` that stages pipeline artifacts to `git add -A -f`, at exactly five locations, and touch nothing else in the file.

**Files:**
- Modify: `.claude/agents/implement-orchestrator.md`

**Context:**
`.claude/agents/implement-orchestrator.md` is a Markdown prompt file (an "agent" system prompt) that an LLM orchestrator agent reads and follows as literal instructions when running `/implement-next-task`. It is not executable code — there is no build, compile step, or test suite for it. It contains several `git add -A` shell-command invocations meant to stage pipeline artifacts under `artifacts/feat-{issue_number}/...` before committing. However, `artifacts/` is listed in the repo's `.gitignore`, so plain `git add -A` silently stages nothing under that path (`-A` does not override `.gitignore`; `-f`/`--force` is required to stage ignored paths). This has already caused real pipeline runs to push commits that were missing their impl/review/code-review artifacts — the bug was only caught after the fact by a downstream `git ls-files --error-unmatch` verification step, i.e. after the broken commit was already pushed.

A sibling issue (#4017) previously fixed this same bug class in three other files (`orchestrator.md`, `plan-orchestrator.md`, `oneshot/SKILL.md`) but explicitly skipped `implement-orchestrator.md` on the (incorrect) assumption that it doesn't stage `artifacts/feat-{issue_number}` content. This task corrects that omission in this one file only.

The fix is purely mechanical: append ` -f` after `-A` in five specific `git add -A` occurrences (three inside fenced ```bash code blocks, two inside inline backtick-quoted command strings). No other text, line, command, or behavior in the file changes. Do NOT touch `orchestrator.md`, `plan-orchestrator.md`, `oneshot/SKILL.md`, or `.gitignore` — those are explicitly out of scope.

There is a sixth, unrelated `git add -A` mention in the file (in prose, describing re-adding an already-tracked file after `git rm -f`/`rm`, with no adjacent `git ls-files --error-unmatch` guard) — this one must NOT be changed; it is not an artifact-staging call site and is already correct as-is with a bare `git add -A`.

**Implementation steps:**

Apply each of the following five edits by exact text match (do not rely on line numbers, which may have shifted slightly; match on the surrounding text shown). Each edit changes only the single `git add -A` occurrence shown — do not alter any other character on the matched lines.

1. **Handling Review Result section — primary commit-and-verify block.**
   Find this exact fenced code block:
   ```bash
   git add -A
   git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md     # STRICT
   git ls-files --error-unmatch artifacts/feat-{issue_number}/review/{task_name}.r{N}.md   # STRICT
   git push
   ```
   Replace with:
   ```bash
   git add -A -f
   git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md     # STRICT
   git ls-files --error-unmatch artifacts/feat-{issue_number}/review/{task_name}.r{N}.md   # STRICT
   git push
   ```
   (Only the first line changes: `git add -A` → `git add -A -f`.)

2. **Handling Review Result section — inline PASS checkpoint commit.**
   Find this exact inline command (it appears inside a **PASS** bullet, parenthesized after "commit the checkpoint update"):
   ```
   git add -A && git commit -m "chore(feat-{issue_number}): {task_name}
     passed review" || true && git push
   ```
   (Note: in the live file this string is wrapped across two lines by the Markdown source's line-wrapping — match on the literal characters, ignoring the line break, i.e. find the substring `git add -A && git commit -m "chore(feat-{issue_number}): {task_name}\n  passed review" || true && git push`.)
   Change only the leading `git add -A` to `git add -A -f`, giving:
   ```
   git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name}
     passed review" || true && git push
   ```

3. **Handling Review Result section — inline failed-after-max-revisions checkpoint commit.**
   Find this exact inline command (it appears in the `N >= max_revisions` sub-case of the **REVISION_NEEDED** bullet, parenthesized after "Commit and push the checkpoint update"):
   ```
   git add -A && git commit -m
     "chore(feat-{issue_number}): {task_name} failed after max revisions"
     || true && git push
   ```
   (Again, match on the literal characters across the wrapped lines: `git add -A && git commit -m\n    "chore(feat-{issue_number}): {task_name} failed after max revisions"\n    || true && git push`.)
   Change only the leading `git add -A` to `git add -A -f`, giving:
   ```
   git add -A -f && git commit -m
     "chore(feat-{issue_number}): {task_name} failed after max revisions"
     || true && git push
   ```
   Do not touch the adjacent `N < max_revisions` sub-case just above it — it only says "commit and push the checkpoint update" in prose, with no literal `git add -A` string, so there is nothing to change there.

4. **Code Review Fix Pass section — commit-and-verify block.**
   Find this exact fenced code block:
   ```bash
   git add -A
   git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/code-review-fixes.r{N}.md   # STRICT
   git push
   ```
   Replace with:
   ```bash
   git add -A -f
   git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/impl/code-review-fixes.r{N}.md   # STRICT
   git push
   ```
   (Only the first line changes.)

5. **Code Review phase section — commit-and-verify block.**
   Find this exact fenced code block:
   ```bash
   git add -A
   git commit -m "chore(feat-{issue_number}): code review r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/code-review.r{N}.md
   git push
   ```
   Replace with:
   ```bash
   git add -A -f
   git commit -m "chore(feat-{issue_number}): code review r{N}" || true
   git ls-files --error-unmatch artifacts/feat-{issue_number}/code-review.r{N}.md
   git push
   ```
   (Only the first line changes.)

6. **Do not edit** the following, which must remain exactly as they are:
   - The `N < max_revisions` sub-case prose ("commit and push the checkpoint update") in the Handling Review Result section — no literal `git add -A` string exists there.
   - The Code Review phase section's CLEAN / `CHANGES_REQUESTED` (`N < max_revisions`) / `CHANGES_REQUESTED` (`N >= max_revisions`) result-handling bullets — these describe committing in prose ("Commit and push", "commit and push it") with no literal `git add -A` string.
   - The prose parenthetical `delete it (`git rm -f` or plain `rm` + `git add -A`) so` in the CLEAN bullet — this describes re-adding an already-tracked (not gitignored) file after deletion, is not an artifact-staging call site, has no adjacent `git ls-files --error-unmatch` guard, and does not need `-f`. Leave this `git add -A` exactly as-is.
   - Everything else in the file (prose, headings, other commands, `.gitignore`, any other file).

**Acceptance criteria:**
- `grep -c 'git add -A -f' .claude/agents/implement-orchestrator.md` equals `5`.
- `grep -c 'git add -A' .claude/agents/implement-orchestrator.md` (counting all occurrences, with or without `-f`) equals `6` — i.e. the 5 newly-flagged occurrences plus the 1 untouched prose occurrence in the CLEAN bullet's `git rm -f`/`git add -A` parenthetical.
- `grep -n 'git add -A[^-]' .claude/agents/implement-orchestrator.md` (bare `git add -A` not immediately followed by `-f`) returns exactly one match, and that match is the CLEAN-bullet prose line containing `` `git rm -f` or plain `rm` + `git add -A`) so ``.
- No other line in the file differs from the version read at task start (i.e. `git diff -- .claude/agents/implement-orchestrator.md` shows only the five ` -f` flag insertions and no other changes — no reformatting, no whitespace changes, no reordering).
- The three fenced ```bash blocks (Handling Review Result / Code Review Fix Pass / Code Review phase) each still contain their `git commit`, `git ls-files --error-unmatch`, and `git push` lines unchanged, immediately after the now-flagged `git add -A -f` line.
- `orchestrator.md`, `plan-orchestrator.md`, `oneshot/SKILL.md`, and `.gitignore` are unmodified by this task.
