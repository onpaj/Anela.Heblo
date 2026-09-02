# Implementation: restore-git-add-f-flags

## What was implemented
Restored the `-f` (force) flag on `git add -A artifacts/feat-{n}` invocations across the pipeline's orchestrator agent prompts and the oneshot skill, fixing a regression of #3980. Without `-f`, `git add -A` silently skips any artifact path matched by a `.gitignore` rule, so generated pipeline artifacts (spec, arch-review, design, task-plan, impl/review files, state.json) could be silently dropped from commits and never reach the PR.

## Files created/modified
- `.claude/agents/orchestrator.md` — 6 occurrences of `git add -A artifacts/feat-{issue_number}` changed to `git add -A -f artifacts/feat-{issue_number}` (replace_all, exact line only)
- `.claude/agents/plan-orchestrator.md` — 3 occurrences of `git add -A artifacts/feat-{issue_number}` changed to `git add -A -f artifacts/feat-{issue_number}` (replace_all, exact line only)
- `.claude/skills/oneshot/SKILL.md` — 1 occurrence of `git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged` changed to `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`; the separate unrelated line `git add -A                              # stage code + everything else` was left untouched as instructed

## Tests
All verification greps from the task were run (with one adjustment — see Notes for the diff-line-count checks).

```
--- orchestrator.md ---
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/orchestrator.md   => 6   (expected 6)
grep -c 'git add -A artifacts/feat-{issue_number}$' .claude/agents/orchestrator.md      => 0   (expected 0)
git diff .claude/agents/orchestrator.md | grep -c '^[+-]'                               => 14  (raw count includes 2 "--- a/file"/"+++ b/file" diff header lines)
git diff .claude/agents/orchestrator.md | grep -E '^[+-][^+-]' | grep -c '^[+-]'         => 12  (matches task's expected count once header lines excluded)

--- plan-orchestrator.md ---
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/plan-orchestrator.md  => 3   (expected 3)
grep -c 'git add -A artifacts/feat-{issue_number}$' .claude/agents/plan-orchestrator.md     => 0   (expected 0)
git diff .claude/agents/plan-orchestrator.md | grep -c '^[+-]'                              => 8   (includes 2 header lines)
git diff .claude/agents/plan-orchestrator.md | grep -E '^[+-][^+-]' | grep -c '^[+-]'        => 6   (matches expected)

--- SKILL.md ---
grep -n 'git add -A -f artifacts/feat-{issue_id}' .claude/skills/oneshot/SKILL.md
  => 150:git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
grep -n 'git add -A artifacts/feat-{issue_id}$' .claude/skills/oneshot/SKILL.md
  => (no output, expected)
grep -n '^git add -A *# stage code' .claude/skills/oneshot/SKILL.md
  => 151:git add -A                              # stage code + everything else  (unchanged, as expected)
git diff .claude/skills/oneshot/SKILL.md | grep -c '^[+-]'                        => 4   (includes 2 header lines)
git diff .claude/skills/oneshot/SKILL.md | grep -E '^[+-][^+-]' | grep -c '^[+-]'  => 2   (matches expected)

--- Step 7: full-repo sweep ---
Literal task pattern `grep -rn 'git add -A artifacts/feat-' .claude/` now returns 0 matches
by construction — after the fix, "git add -A" and "artifacts/feat-" are no longer
adjacent substrings (there is now "-f " between them), so the literal pattern given
in the task no longer matches post-fix lines. Re-ran with a pattern spanning both
fixed and unfixed forms to get the real sweep result:

grep -rnE 'git add -A( -f)? artifacts/feat-' .claude/
  => 10 matching lines total (6 in orchestrator.md, 3 in plan-orchestrator.md, 1 in SKILL.md)
  => every line contains " -f " between "-A" and "artifacts/feat-"
  => zero bare (unfixed) occurrences found anywhere under .claude/
```

## How to verify
```bash
cd /home/user/worktrees/feature-4017-Agentharness-Pipeline-Git-Add-A-Artifacts-Feat-N-R
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/orchestrator.md      # 6
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/plan-orchestrator.md  # 3
grep -n 'git add -A -f artifacts/feat-{issue_id}' .claude/skills/oneshot/SKILL.md          # 1 match
grep -rnE 'git add -A( -f)? artifacts/feat-' .claude/ | grep -v ' -f '                     # no output (no bare occurrences)
git show --stat HEAD                                                                        # 3 files changed, 10 insertions(+), 10 deletions(-)
```

## Notes
- The task's Step 7 verification command as literally written (`grep -rn 'git add -A artifacts/feat-'`) stops matching after the fix is applied, since inserting `-f` breaks the contiguous substring `-A artifacts/feat-` it searches for. I substituted an equivalent regex (`git add -A( -f)? artifacts/feat-`) to actually perform the intended sweep, and confirmed: 10 total occurrences, all fixed, zero bare ones remain anywhere under `.claude/`. This is a quirk in the task's own verification command, not a deviation in the actual edits.
- Similarly, the raw `git diff <file> | grep -c '^[+-]'` counts from Steps 2/4/6 came out 2 higher than the task's expected numbers (14 vs 12, 8 vs 6, 4 vs 2) because that grep also matches each diff's `--- a/file` / `+++ b/file` header lines. Filtering those out (`grep -E '^[+-][^+-]'`) reproduces exactly the task's expected counts (12, 6, 2), confirming only the intended lines changed and nothing else in any of the three files.
- No other content (commit messages, `ls-files` verification lines, prose, headers, step numbering, the unrelated `git add -A # stage code...` line) was touched — verified by full diff inspection of all three files.
- No application code was touched; this is a documentation/template-only fix, consistent with the task's own stated validation scope (the greps above, no build/test suite).

## PR Summary
Restores the `-f` flag that had regressed off `git add -A artifacts/feat-{n}` across the AgentHarness pipeline's orchestrator and plan-orchestrator agent prompts, and the oneshot skill's PR-finishing step. Without `-f`, any artifact path shadowed by a `.gitignore` rule is silently skipped by `git add -A`, so pipeline-generated artifacts (spec, arch-review, design, task-plan, per-task impl/review files, state.json) could fail to reach the feature branch and the PR — this is the regression of #3980. The fix is a literal string substitution at each occurrence; no other content in any of the three files was changed.

### Changes
- `.claude/agents/orchestrator.md` — 6 occurrences: `git add -A artifacts/feat-{issue_number}` → `git add -A -f artifacts/feat-{issue_number}`
- `.claude/agents/plan-orchestrator.md` — 3 occurrences: `git add -A artifacts/feat-{issue_number}` → `git add -A -f artifacts/feat-{issue_number}`
- `.claude/skills/oneshot/SKILL.md` — 1 occurrence: `git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged` → `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`

## Status
DONE
