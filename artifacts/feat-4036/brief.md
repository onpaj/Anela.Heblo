## What happened

While running `/implement-next-task` for issue #4017 (the very fix for this
bug class in `orchestrator.md`/`plan-orchestrator.md`/`oneshot/SKILL.md`),
the Code Review phase step of `.claude/agents/implement-orchestrator.md`
hit the identical failure mode it is meant to guard against.

`implement-orchestrator.md`'s **Code Review phase** section instructs:

```bash
git add -A
git commit -m "chore(feat-{issue_number}): code review r{N}" || true
git ls-files --error-unmatch artifacts/feat-{issue_number}/code-review.r{N}.md
git push
```

Since `artifacts/` is `.gitignore`d, `git add -A` (without `-f`) silently
stages nothing under `artifacts/feat-4017/`. Running this exactly as
written produced a commit (`chore(feat-4017): code review r1`) containing
only an unrelated `state.json` change — `code-review.r1.md` itself was
never staged, and the following `git ls-files --error-unmatch` line (which
exists specifically to catch this) correctly failed:

```
error: pathspec 'artifacts/feat-4017/code-review.r1.md' did not match any file(s) known to git
```

The commit had *already been pushed* before the hard-verify ran (the
template pushes unconditionally after the commit line, with the
`ls-files` check positioned after `git commit` but the push is still
listed as the final step regardless of that check's outcome in practice),
so the fix required an explicit follow-up `git add -A -f
artifacts/feat-4017` + commit + push to actually land the artifact.

This is not hypothetical or first-occurrence: the same worktree's git log
shows a prior invocation of this exact orchestrator template hit the same
bug on the **developer task** commit step (`Handling Review Result`
section, same `git add -A` pattern) and required an identical manual
follow-up commit, visible as:

```
102c830 chore(feat-4017): impl+review for restore-git-add-f-flags r1
d4bfa1d chore(feat-4017): impl+review for restore-git-add-f-flags r1 (artifacts)
```

where `102c830` is the broken bare-`git add -A` commit (missing the impl/
and review/ artifacts) and `d4bfa1d` is the manual patch-up.

## Root cause

Every commit step in `.claude/agents/implement-orchestrator.md` that
stages `artifacts/feat-{issue_number}/...` files uses bare `git add -A`
instead of `git add -A -f`, in these sections:

- **Handling Review Result** (`impl/{task_name}.r{N}.md`,
  `review/{task_name}.r{N}.md`, and the PASS/REVISION_NEEDED checkpoint
  commits)
- **Code Review Fix Pass** (`impl/code-review-fixes.r{N}.md`)
- **Code Review phase** (`code-review.r{N}.md`, and the phase-status
  commits)

This is the same root cause issue #4017 fixes in `orchestrator.md`,
`plan-orchestrator.md`, and `oneshot/SKILL.md` — `implement-orchestrator.md`
was explicitly listed as out of scope for #4017 ("none of those stage
`artifacts/feat-{n}`"), but that exclusion is incorrect: it does stage
`artifacts/feat-{n}` and has the exact same bug.

## Impact

Every `/implement-next-task` invocation that follows
`implement-orchestrator.md` literally risks silently failing to commit its
own impl/review/code-review artifacts on the first attempt of each unit,
relying entirely on the `git ls-files --error-unmatch` hard-verify to
catch it after the fact — and even then, only if the invocation notices
the failure and manually patches it up with a scoped `-f` add, rather than
following the template's own (broken) instruction. A session that doesn't
notice, or that treats the `git commit ... || true` as fully non-fatal
without checking the verify step's exit code, would push an artifact-less
commit and move on.

## Fix

Add `-f` to every `git add -A` in `.claude/agents/implement-orchestrator.md`
that is followed by a `git ls-files --error-unmatch artifacts/feat-{n}/...`
verification, matching the fix already applied to the other three files by
#4017 / this issue's sibling PR. Likely occurrences (verify by content
match against the current file, not by line number):

- `git add -A` → `git add -A -f` in **Handling Review Result**'s commit
  block and its PASS/REVISION_NEEDED/failed checkpoint-commit lines
  (`git add -A && git commit ...`)
- `git add -A` → `git add -A -f` in **Code Review Fix Pass**'s commit block
- `git add -A` → `git add -A -f` in **Code Review phase**'s commit block
  and its CLEAN/CHANGES_REQUESTED result-handling commit lines

Same scope discipline as #4017: pure flag insertion, no other changes to
these files.

Found while running `/implement-next-task` for issue #4017 itself — the
Code Review phase step failed exactly as described above, and was
manually patched around rather than following the current broken
instruction, so the pipeline could proceed.
