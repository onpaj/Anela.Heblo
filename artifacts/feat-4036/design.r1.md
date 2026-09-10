# Design: Fix missing `-f` flag on `git add -A` in `implement-orchestrator.md`

## Component Design

There are no software components, services, or modules involved. The sole artifact touched is the single Markdown prompt file `.claude/agents/implement-orchestrator.md`, which is read and executed as instructions by the implementing-stage orchestrator agent. The "component" here is the set of five literal text edits described below.

Edit locations (verified against the live file by the architecture review; match by surrounding text content, not by line number, since line numbers drift):

1. **Handling Review Result** section — primary commit-and-verify fenced `bash` block (FR-1)
   - Change the `git add -A` line immediately preceding `git commit -m "chore(feat-{issue_number}): impl+review for {task_name} r{N}" || true` to `git add -A -f`.
   - No other line in the block (`git commit`, the two `git ls-files --error-unmatch` STRICT checks, `git push`) changes.

2. **Handling Review Result** section — inline **PASS** checkpoint commit (FR-2)
   - Change `` `git add -A && git commit -m "chore(feat-{issue_number}): {task_name} passed review" || true && git push` `` to `` `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} passed review" || true && git push` ``.

3. **Handling Review Result** section — inline failed-checkpoint commit under **REVISION_NEEDED** / `N >= max_revisions` (FR-3)
   - Change `` `git add -A && git commit -m "chore(feat-{issue_number}): {task_name} failed after max revisions" || true && git push` `` to `` `git add -A -f && git commit -m "chore(feat-{issue_number}): {task_name} failed after max revisions" || true && git push` ``.
   - The adjacent `N < max_revisions` sub-case, which only describes committing in prose with no literal `git add -A` string, is left untouched.

4. **Code Review Fix Pass** section — commit-and-verify fenced `bash` block (FR-4)
   - Change the `git add -A` line immediately preceding `git commit -m "chore(feat-{issue_number}): code review fix r{N}" || true` to `git add -A -f`.
   - No other line in the block changes.

5. **Code Review phase** section — commit-and-verify fenced `bash` block (FR-5)
   - Change the `git add -A` line immediately preceding `git commit -m "chore(feat-{issue_number}): code review r{N}" || true` to `git add -A -f`.
   - No other line in the block changes.

**Explicitly not edited (FR-6, confirmation-only):**
- The **Code Review phase** section's CLEAN / CHANGES_REQUESTED (`N < max_revisions`) / CHANGES_REQUESTED (`N >= max_revisions`) result-handling bullets describe committing in prose ("Commit and push", "commit and push it") and contain no literal `git add -A` string — confirmed directly against the live file (~lines 239–265). No edit is made there.
- The prose mention near line 243 (`git rm -f` or plain `rm` + `git add -A`) describes re-adding an already-tracked (not gitignored) file after deletion — it is not a new artifact-staging occurrence, has no adjacent `git ls-files --error-unmatch` guard, and does not need `-f`. Leave it untouched.

**Verification approach:** after editing, `grep -c "git add -A -f"` on the file should equal 5, and `grep -c "git add -A"` for the bare (no `-f`) form should equal 1 (only the line-243 prose reference, plus the untouched `N < max_revisions` prose bullet has no literal string to match at all).

## Data Schemas

Not applicable. This is a text edit to a Markdown prompt-template file, not a code or data change — there are no database schemas, API request/response shapes, or event payloads involved. The only "shape" affected is the shell-command text embedded in the file, and that shape is fully pinned verbatim by `spec.r1.md` FR-1 through FR-5 (before/after blocks and inline strings), reproduced above.
