# Code Review: restore-git-add-f-flags

## Summary
The implementation correctly restores the `-f` (force) flag on all `git add -A artifacts/feat-{n}` invocations across three orchestrator/skill template files. All 10 target occurrences (6 in orchestrator.md, 3 in plan-orchestrator.md, 1 in oneshot/SKILL.md) have been updated with the flag, the unrelated bare `git add -A` line was correctly left untouched, and no other content in any of the three files was modified.

## Review Result: PASS

### task: restore-git-add-f-flags
**Status:** PASS

## Verification Summary

**Commit scope:** Exactly 3 files modified as required:
- `.claude/agents/orchestrator.md` (12 insertions/deletions: 6 lines changed)
- `.claude/agents/plan-orchestrator.md` (6 insertions/deletions: 3 lines changed)
- `.claude/skills/oneshot/SKILL.md` (2 insertions/deletions: 1 line changed)

**Target line updates:**
- orchestrator.md: 6/6 occurrences of `git add -A artifacts/feat-{issue_number}` → `git add -A -f artifacts/feat-{issue_number}` ✓
- plan-orchestrator.md: 3/3 occurrences of `git add -A artifacts/feat-{issue_number}` → `git add -A -f artifacts/feat-{issue_number}` ✓
- oneshot/SKILL.md: 1/1 occurrence of `git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged` → `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged` ✓

**Unrelated line preservation:**
- Line 151 in oneshot/SKILL.md (`git add -A                              # stage code + everything else`) remains unchanged ✓

**Full-repo sweep results:**
- No bare/unfixed `git add -A artifacts/feat-` occurrences remain in .claude/ ✓
- Exactly 10 total `git add -A -f artifacts/feat-` occurrences found (6 + 3 + 1) ✓
- All 10 occurrences contain the `-f` flag ✓

**Diff inspection:** Only the exact target lines were modified; no other lines, headers, step numbering, commit messages, prose, or ls-files verification lines were touched.

**Status:** PASS
