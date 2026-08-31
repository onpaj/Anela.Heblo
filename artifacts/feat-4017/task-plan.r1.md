# Task Plan: Restore `-f` flag on `git add -A artifacts/feat-{n}`

**Goal:** Restore the `-f` (force) flag on all ten `git add -A artifacts/feat-{n}` invocations across `.claude/agents/orchestrator.md`, `.claude/agents/plan-orchestrator.md`, and `.claude/skills/oneshot/SKILL.md`, reverting the regression introduced by commit `72784c2` and matching exactly the state PR #3991 established.

**Architecture:** No architecture change — per `arch-review.r1.md` this is a pure text restoration inside three prompt/instruction templates, with zero structural footprint (no new component, module, interface, or data flow). The fix inserts the literal substring ` -f` immediately after `-A` and before the `artifacts/feat-{...}` path argument at exactly ten call sites, and touches nothing else in any of the three files.

---

### task: restore-git-add-f-flags

**Files:**
- Modify: `.claude/agents/orchestrator.md` (6 occurrences)
- Modify: `.claude/agents/plan-orchestrator.md` (3 occurrences)
- Modify: `.claude/skills/oneshot/SKILL.md` (1 occurrence)

- [ ] **Step 1: Fix `.claude/agents/orchestrator.md`**

All 6 occurrences are the identical literal line (at lines 32, 75, 101, 142, 166, 205 as of the current worktree — locate by content match, not line number, since the file may have shifted):

Current line (appears 6 times):
```
git add -A artifacts/feat-{issue_number}
```

Fixed line (replaces every occurrence):
```
git add -A -f artifacts/feat-{issue_number}
```

Since the line text is byte-identical at all 6 sites, use the Edit tool with `replace_all: true`:
- `old_string`: `git add -A artifacts/feat-{issue_number}`
- `new_string`: `git add -A -f artifacts/feat-{issue_number}`
- `replace_all`: `true`

This is safe because the file contains no other line matching this exact text (the surrounding context — commit/ls-files lines — differs around each occurrence, but the target line itself is identical and is the only thing being replaced).

Do not touch any other line (commit messages, `ls-files` verification lines, prose, headers, or step numbering must show zero diff).

- [ ] **Step 2: Verify `.claude/agents/orchestrator.md`**

Run:
```bash
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/orchestrator.md
```
Expected: `6`

Run:
```bash
grep -c 'git add -A artifacts/feat-{issue_number}$' .claude/agents/orchestrator.md
```
Expected: `0` (no unfixed line — the pattern anchors to end-of-line immediately after the path, so it will not match the now-fixed `-f` lines, only leftover bare occurrences)

Run:
```bash
git diff .claude/agents/orchestrator.md | grep -c '^[+-]'
```
Expected: `12` (6 removed lines + 6 added lines — confirms only the six target lines changed, nothing else in the file).

- [ ] **Step 3: Fix `.claude/agents/plan-orchestrator.md`**

All 3 occurrences are the identical literal line (at lines 44, 102, 142 as of the current worktree — locate by content match, not line number):

Current line (appears 3 times):
```
git add -A artifacts/feat-{issue_number}
```

Fixed line (replaces every occurrence):
```
git add -A -f artifacts/feat-{issue_number}
```

Use the Edit tool with `replace_all: true`:
- `old_string`: `git add -A artifacts/feat-{issue_number}`
- `new_string`: `git add -A -f artifacts/feat-{issue_number}`
- `replace_all`: `true`

Do not touch the `git push` lines, `ls-files` verification lines, non-fast-forward guidance prose, or any other content in this file.

- [ ] **Step 4: Verify `.claude/agents/plan-orchestrator.md`**

Run:
```bash
grep -c 'git add -A -f artifacts/feat-{issue_number}' .claude/agents/plan-orchestrator.md
```
Expected: `3`

Run:
```bash
grep -c 'git add -A artifacts/feat-{issue_number}$' .claude/agents/plan-orchestrator.md
```
Expected: `0`

Run:
```bash
git diff .claude/agents/plan-orchestrator.md | grep -c '^[+-]'
```
Expected: `6` (3 removed + 3 added — confirms only the three target lines changed).

- [ ] **Step 5: Fix `.claude/skills/oneshot/SKILL.md`**

Line 150 currently reads exactly:
```
git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```

Change it to exactly (trailing comment and spacing preserved verbatim — same four-space gap before the `#`):
```
git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
```

Use the Edit tool (this exact line is unique in the file, so no `replace_all` needed):
- `old_string`: `git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`
- `new_string`: `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`

**Do NOT touch line 151**, which currently reads:
```
git add -A                              # stage code + everything else
```
This is a separate, unscoped `git add -A` for staging code changes generally — it is not artifacts-specific, was never touched by PR #3991, and must remain exactly as-is (no `-f`, no path argument).

- [ ] **Step 6: Verify `.claude/skills/oneshot/SKILL.md`**

Run:
```bash
grep -n 'git add -A -f artifacts/feat-{issue_id}' .claude/skills/oneshot/SKILL.md
```
Expected: one match, reading exactly `git add -A -f artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged`

Run:
```bash
grep -n 'git add -A artifacts/feat-{issue_id}$' .claude/skills/oneshot/SKILL.md
```
Expected: no output (zero matches — confirms no unfixed bare form remains; note this pattern intentionally does not match line 151, which has no `artifacts/feat-{issue_id}` path).

Run:
```bash
grep -n '^git add -A *# stage code' .claude/skills/oneshot/SKILL.md
```
Expected: one match — confirms line 151 (`git add -A                              # stage code + everything else`) is unchanged, still has no `-f` and no path argument.

Run:
```bash
git diff .claude/skills/oneshot/SKILL.md | grep -c '^[+-]'
```
Expected: `2` (1 removed + 1 added — confirms only line 150 changed).

- [ ] **Step 7: Full-repo sweep (FR-4)**

Run:
```bash
grep -rn 'git add -A artifacts/feat-' .claude/
```
Expected: exactly 10 matching lines total, every one containing ` -f ` immediately after `git add`, i.e. every match reads `git add -A -f artifacts/feat-...`. Zero matches of the bare `git add -A artifacts/feat-` form (without `-f`) anywhere under `.claude/`.

If the sweep finds a different count than 10, or finds a bare occurrence outside the three files already fixed, stop and treat it as a blocker rather than silently expanding scope — per spec FR-4 and NFR-1, this fix covers exactly these ten call sites.

- [ ] **Step 8: Commit**

This is a documentation/template-only fix — no application code changes, and no test suite runs beyond the grep verification in Steps 2, 4, 6, and 7 above.

```bash
git add .claude/agents/orchestrator.md .claude/agents/plan-orchestrator.md .claude/skills/oneshot/SKILL.md
git commit -m "fix: restore -f flag on git add -A artifacts/feat-{n} (regression of #3980)"
```
