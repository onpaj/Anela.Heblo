---
name: rework
description: Pick up the oldest open PR labelled `needs-work` — one /automerge itself rejected — revise it using the review that rejected it, and push the fix. Use when the user says "rework", "revise needs-work PRs", "fix up the needs-work backlog", or asks to act on a rejected agent PR.
---

You autonomously revise one PR that `/automerge` already rejected. You find
the oldest eligible `needs-work` PR, read the review that rejected it, fix
the code it describes, and push the fix — clearing the way for `/automerge`
to reconsider it next time it runs.

**All deterministic work is done by the scripts beside this file.** Do not
re-implement their logic or hand-write the `gh` commands they already own.
Your only judgement call is reading the review and fixing the code.

One PR per invocation. Run this skill again for the next one.

## 1. Find the candidate

```bash
.claude/skills/rework/find_candidate.sh > /tmp/rework-candidate.json
```

Writes `{"candidate": {...}|null, "skipped": [...]}` to
`/tmp/rework-candidate.json`. `candidate` is the oldest open `needs-work` PR
that has not hit the revision-attempt cap; `skipped` lists PRs that have and
will never be picked. Do not second-guess the cap or try to rescue a skipped
PR. Read fields out of this file with `jq` in the steps below — never
interpolate PR-derived text (like `headRefName`) directly into a shell
string.

If `candidate` is `null` (`jq -e '.candidate == null' /tmp/rework-candidate.json`),
print `No needs-work PRs ready to revise.`, list `skipped` with reasons, and
stop.

## 2. Check out the PR's branch

The PR's branch already exists — it was created by `oneshot`. Reuse its
worktree convention rather than creating a new branch:

```bash
HEAD_REF=$(jq -r '.candidate.headRefName' /tmp/rework-candidate.json)
WORKTREE="../worktrees/$(echo "$HEAD_REF" | sed 's#/#-#')"
```

Before touching the branch, confirm the PR is still open — it may have been
merged or closed by a human between step 1 and now:

```bash
gh pr view {N} --json state --jq .state
```

If the result is not `OPEN`, report this PR as skipped (not pushed to) and
**stop** — do not proceed to step 3 or beyond.

Otherwise, check out the branch:

```bash
if [ -d "$WORKTREE" ]; then
  git -C "$WORKTREE" fetch origin "$HEAD_REF"
  git -C "$WORKTREE" reset --hard "origin/$HEAD_REF"
else
  git worktree add "$WORKTREE" "$HEAD_REF"
fi
```

All edits, commits, and the push happen inside `$WORKTREE` — never against
the primary checkout.

## 3. Read the feedback

Gather the PR's full review history before touching any code — not just the
latest `/automerge` block, so context from earlier rounds or a human's inline
notes is not lost:

```bash
gh pr view {N} --json title,body,comments,reviews
gh api repos/{owner}/{repo}/pulls/{N}/comments
gh pr diff {N}
```

## 4. Revise the code

Read the feedback gathered above, identify the concrete issues it describes,
and fix them directly in `$WORKTREE` — this is the one part of this skill
that is not scripted, the same way `/automerge`'s scoring is not scripted:
judging what a review means requires the model. If the feedback is too vague
to act on directly, make a good-faith improvement (add the missing test,
clarify the ambiguous logic) rather than aborting.

## 5. Commit and push

Stage only the files you actually changed — never `git add -A`. Commit with
a message summarizing what was addressed, and push to the PR's existing
branch:

```bash
git -C "$WORKTREE" add <files>
git -C "$WORKTREE" commit -m "fix: address /automerge review feedback"
git -C "$WORKTREE" push origin "HEAD:$HEAD_REF"
```

If the push fails, report the failure and **stop** — do not call
`finish_revision.sh`. `needs-work` must stay on a PR whose fix did not
actually land.

## 6. Finish

Write a short summary of what you changed to a file using the **Write
tool** — never interpolate it into a shell command — then:

```bash
.claude/skills/rework/finish_revision.sh --pr {N} --summary-file /tmp/rework-{N}-summary.md
```

This posts the summary as a PR comment and removes `needs-work`. On success,
remove the worktree:

```bash
git worktree remove "$WORKTREE"
```

## 7. Report

State which PR you revised, what you changed, and the `skipped` list from
step 1 with reasons — a PR sitting at the revision cap needs a human to look
at it.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MAX_REVISION_ATTEMPTS` | `find_candidate.sh` |
| `NEEDS_WORK_LABEL` | `find_candidate.sh`, `finish_revision.sh` (must match `automerge/apply_verdict.sh`'s copy) |

## Limits worth knowing

This skill's revision is not independently reviewed before `needs-work`
comes off — the next signal is whatever `/automerge` says next time it runs.
A confidently-wrong revision looks identical to a correct one until then.
There is no confirmation prompt. Watch the first few runs.

The revision-attempt cap counts prior `/automerge` rejections (`verdict:
REJECT` comments), not `/rework` runs — a PR a human re-labelled
`needs-work` by hand always looks like zero prior attempts to this skill.
