# Code Review: feat-4036 (Implement-Orchestrator.md `git add -A -f`)

## Review Result: CLEAN

### Blocking
- None

### Advisory
- None

## Summary

Verified directly against `.claude/agents/implement-orchestrator.md`
(`git diff origin/main...HEAD -- .claude/agents/implement-orchestrator.md`):
exactly five hunks, each substituting `git add -A` → `git add -A -f`, and no
other line in the file was touched.

**FR-1 through FR-5** (the five required `-f` insertions) — all present and
correctly scoped:
1. Handling Review Result fenced block (line 91): `git add -A -f` ✓
2. PASS bullet inline checkpoint commit (line 109): `git add -A -f && git commit ...` ✓
3. Failed-after-max-revisions inline checkpoint commit (line 133): `git add -A -f && git commit ...` ✓
4. Code Review Fix Pass fenced block (line 170): `git add -A -f` ✓
5. Code Review phase fenced block (line 225): `git add -A -f` ✓

In each case the surrounding `git commit`, `git ls-files --error-unmatch`,
and `git push` lines are byte-for-byte unchanged, and the commit messages are
untouched.

**FR-6** (result-handling bullets must be left alone) — confirmed. The
CLEAN / `N < max_revisions` / `N >= max_revisions` bullets in the Code Review
phase's step 7 are prose-only, as expected, with one nuance: the CLEAN bullet
does contain a literal `git add -A` substring (line 243, `` `git rm -f` or
plain `rm` + `git add -A` ``), contrary to FR-6's background assertion that
no literal string exists there. This is correctly left unflagged: that
occurrence describes re-staging the *deletion* of an already-tracked file
(`task-context/code-review-fixes.md`), and `.gitignore` only suppresses
staging of new/untracked paths — removals of already-tracked, gitignored
paths stage correctly with plain `git add -A` (or `git rm -f`), so `-f` is
not required there. The impl and prior review artifacts both document this
reasoning explicitly, and it holds up under inspection. FR-6's letter (“no
literal string exists”) is technically inaccurate, but its intent (don't add
`-f` where it isn't needed) was correctly honored — not a defect in the
diff.

**No unintended changes**: `git status --porcelain` shows only
`artifacts/feat-4036/state.json` modified outside the target file (pipeline
bookkeeping, out of scope per task instructions). `.gitignore`,
`orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md` are
untouched, consistent with the spec's Out of Scope section.

**Mirrors #4017's pattern**: confirmed `-f` is inserted immediately after
`-A` with a single space (`git add -A -f`), the same form #4017 used. Note
for context only (not a defect in this diff): the sibling files
`orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md` currently
do *not* carry the `-f` flag on `git add -A` in this worktree's history — a
later, unrelated commit (`72784c2`, "chore: update orchestrator and oneshot
skill templates (remove -f from git add)", applied by a session-start hook)
reverted #4017's fix on those three files. That regression is out of scope
for #4036 (which only targets `implement-orchestrator.md`, never touched by
that revert), but the same bug class may currently be live again in those
three siblings — worth a follow-up issue if not already tracked.

## Plan Alignment
Matches `spec.r1.md` exactly: all 5 FRs satisfied, FR-6 honored in spirit,
Out of Scope items respected, no other prose/commit-message/logic changes.
Pure surgical flag insertion, consistent with CLAUDE.md's "surgical changes"
principle.

## Code Quality
N/A beyond the above — this is a single-flag edit in a Markdown
prompt-template file with no runtime code, tests, or build surface.

**Status:** PASS
