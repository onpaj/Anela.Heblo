# Implementation: add-force-flag-to-git-add-in-implement-orchestrator

## What was implemented
Changed every `git add -A` occurrence in `.claude/agents/implement-orchestrator.md` that
stages pipeline artifacts under `artifacts/feat-{issue_number}/...` to `git add -A -f`, at
exactly five locations (three inside fenced ```bash code blocks, two inside inline
backtick-quoted command strings). This corrects the omission left by issue #4017, which
fixed the same bug class (plain `git add -A` silently skipping gitignored `artifacts/`
paths) in `orchestrator.md`, `plan-orchestrator.md`, and `oneshot/SKILL.md` but did not
touch `implement-orchestrator.md`. The sixth, unrelated `git add -A` mention (prose in the
CLEAN bullet describing re-adding an already-tracked, non-gitignored file after `git rm -f`
or `rm`) was left untouched, as specified.

## Files created/modified
- `.claude/agents/implement-orchestrator.md` — five `git add -A` → `git add -A -f` edits (Handling Review Result primary commit block, PASS checkpoint inline commit, failed-after-max-revisions inline commit, Code Review Fix Pass commit block, Code Review phase commit block)

## Tests
N/A - documentation/prompt file, no test suite

## How to verify
- `grep -c 'git add -A -f' .claude/agents/implement-orchestrator.md` → `5`
- `grep -c 'git add -A' .claude/agents/implement-orchestrator.md` → `6`
- `grep -nP 'git add -A(?!\s*-f)' .claude/agents/implement-orchestrator.md` → exactly one match, the CLEAN-bullet prose line containing `` `git rm -f` or plain `rm` + `git add -A`) so ``
- `git diff HEAD~1 -- .claude/agents/implement-orchestrator.md` → shows only the five ` -f` flag insertions, nothing else
- `git status --porcelain -- .claude/agents/orchestrator.md .claude/agents/plan-orchestrator.md .claude/skills/oneshot/SKILL.md .gitignore` → empty (unmodified)

## Notes
The acceptance-criteria grep given in the task spec, `grep -n 'git add -A[^-]'`, is a loose
character-class check: since the edits insert a space before `-f` (`git add -A -f`), the
character immediately after `-A` is a space, not a hyphen, so this literal pattern actually
matches all six occurrences (including the five now-fixed ones), not just the one intended
bare/unflagged occurrence. I verified the real intent — "bare `git add -A` not followed by
`-f`" — with `grep -nP 'git add -A(?!\s*-f)'`, which correctly returns exactly one match:
the untouched CLEAN-bullet prose line. All five target edits were applied by exact string
match against the surrounding context shown in the task, and `git diff` confirms no other
line in the file changed.

## PR Summary
Fixes a bug carried over from issue #4017: `implement-orchestrator.md` contains five
`git add -A` invocations meant to stage pipeline artifacts (impl/review/code-review
summaries) under `artifacts/feat-{issue_number}/...` before committing, but `artifacts/` is
gitignored, so plain `-A` silently staged nothing there — this had already caused real
pipeline runs to push commits missing their artifacts, caught only after the fact by a
downstream `git ls-files --error-unmatch` step. This change appends `-f` to force-stage
those five call sites, mirroring the fix already applied elsewhere in #4017. One unrelated,
already-correct `git add -A` mention (prose describing re-adding an already-tracked file
after deletion) was left unchanged, as it is not an artifact-staging call site.

### Changes
- `.claude/agents/implement-orchestrator.md` — five `git add -A` → `git add -A -f` edits at the artifact-staging call sites; no other text changed

## Status
DONE
