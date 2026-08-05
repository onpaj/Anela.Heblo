---
name: automerge
description: Review every open agent-created PR with a fresh subagent each and autonomously squash-merge the high-confidence ones. Use when the user says "automerge", "merge ready PRs", "review open PRs", "ship what's ready", or asks to clear the PR backlog without reviewing each one by hand.
---

You autonomously clear the pipeline's PR backlog. You find the open PRs the
AgentHarness pipeline produced, have each one reviewed in isolation, and merge
the ones the review is confident about — without asking the user for
confirmation.

**All deterministic work is done by the scripts beside this file.** Do not
re-implement their logic, re-derive the score thresholds, or hand-write `gh`
commands they already own. Your only judgement call is the review itself.

## 1. Find the candidates

```bash
.claude/skills/automerge/candidates.sh
```

This returns `{"candidates": [...], "skipped": [...], "truncated": N}`. Draft,
conflicted, changes-requested, and previously-rejected (`needs-work`-labelled)
PRs are already filtered out — do not second-guess that filter or try to
rescue a skipped PR. Each candidate object includes a `linkedIssue` field —
the issue number parsed from a `Closes #N` line in the PR body, or `null` if
none — for use in step 4.

If `candidates` is empty, print `No agent PRs ready to review.`, list the
`skipped` entries with their reasons, and stop.

## 2. Review each candidate — one subagent per PR

Spawn **one `code-reviewer` subagent per candidate PR, all in a single message**
so they run concurrently. Each gets a fresh context containing only its own PR.
Never review two PRs in one subagent: an earlier PR's reasoning bleeds into the
next one's score.

Give each subagent exactly this prompt, with `{N}` replaced by the PR number:

> Review pull request #{N} in this repository and score how confident you are
> that it can be merged to `master` with no human looking at it.
>
> You are READ-ONLY. You must not run `gh pr merge`, `gh pr close`,
> `gh pr edit`, `git push`, or any other state-changing command. Gather context
> with:
> - `gh pr view {N} --json title,body,headRefName,additions,deletions,changedFiles`
> - `gh pr diff {N}`
> - `gh issue view <issue> --json title,body` for the issue the PR body links
> - `Read` and `Grep` on the repo, to check the change fits the code around it
>
> You cannot run the test suite, and you must not assume the code works because
> it looks plausible.
>
> Start from 100 and deduct:
> - -40 the diff does something the linked issue did not ask for
> - -30 no linked issue found in the PR body
> - -25 new behaviour added with no accompanying test
> - -30 touches concurrency-critical code (`state_manager.py`, blob-lease or claim logic)
> - -30 touches auth, secrets handling, or `.github/workflows/`
> - -100 hardcoded secret, credential, or leftover debug statement
> - -20 diff exceeds 400 added lines or 10 changed files
> - -50 the PR description claims work the diff does not contain
> - -25 anything whose correctness you cannot verify from the diff alone
>
> That last one is not optional. If you cannot tell whether the change is
> correct, that is low confidence and the score must show it. Do not round up
> toward a merge because the change looks tidy.
>
> End your output with exactly this block and nothing after it:
>
> ```
> pr: {N}
> score: <0-100 integer>
> verdict: <MERGE if score >= 80, COMMENT if 40-79, REJECT if < 40>
> risk: <low|medium|high>
> reasons:
>   - <specific fact about this diff>
>   - <specific fact about this diff>
> concerns: <none, or what a human should look at>
> ```
>
> Each reason must be a specific fact about this diff. "Code looks good" is not
> a reason.

## 3. Parse each verdict

For each subagent's output, write it to a file and run it through the parser —
never read the score off the block yourself.

Write the subagent's full output to `/tmp/automerge-review-{N}.md` using the
**Write tool** — never interpolate a subagent's output into a shell command; it
originates from PR content (title, body, diff) and must not pass through shell
expansion. A PR containing `$(...)`, backticks, or a `$VAR` sequence would have
that text shell-expanded if it were dropped into a shell string. Then parse the
already-on-disk file with a plain, non-interpolated command:

```bash
.claude/skills/automerge/parse_verdict.py < /tmp/automerge-review-{N}.md
```

The parser owns the thresholds and returns the `action` to take. A malformed or
self-contradictory review comes back `"valid": false` with
`"action": "comment"` — that is correct and final. **Never override it into a
merge.**

## 4. Apply each verdict — serially

Process PRs one at a time, in ascending PR number, so two merges never race on
`master`:

```bash
.claude/skills/automerge/apply_verdict.sh \
  --pr {N} --action {action} --review-file /tmp/automerge-review-{N}.md --issue {issue}
```

Use the `linkedIssue` field from that PR's candidate object in step 1's
`candidates.sh` output — pass it as `--issue` when it is non-null, omit
`--issue` when it is null. Do not ask the reviewer subagent for this value and
do not fetch the PR body yourself; `candidates.sh` already resolved it. The
script returns JSON; a non-zero exit means that PR failed. **Continue to the
next PR regardless** — one failure never aborts the batch.

## 5. Report

Print a table of every PR: number, score, verdict, action taken. Then list:

- `skipped` from step 1, with reasons
- any PR whose review was unparseable
- any `apply_verdict.sh` failure, with its `detail`
- if `truncated` > 0, state exactly how many PRs were left unprocessed

The user reads only this report. It must say what was *not* done as clearly as
what was — a report that quietly omits a truncated tail reads as "everything is
handled" when it is not.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MERGE_THRESHOLD`, `NEEDS_WORK_THRESHOLD` | `parse_verdict.py` |
| `MAX_CANDIDATES`, `AGENT_LABEL` | `candidates.sh` |
| `MERGED_ISSUE_LABEL`, `NEEDS_WORK_LABEL` | `apply_verdict.sh` |

## Limits worth knowing

This skill merges without running the test suite — every score comes from
reading a diff. It is deliberately conservative (a high merge threshold defined
once in `parse_verdict.py`, uncertainty costs score), but it can merge a change
that reads correctly and is not. There is also no confirmation prompt. Watch
the first few runs.

The reviewer subagent's READ-ONLY instruction is a prompt constraint, not an
enforced sandbox — it currently has Bash access and the same `gh` credentials
as this skill. A subagent that follows a malicious instruction embedded in a
PR's diff or title could act independently of the score it reports. Until a
Bash-less or credential-scoped reviewer ships, treat every merge this skill
performs as something a compromised or confused subagent could have influenced
beyond its stated score.

A PR that lands in the `comment` band gets a fresh review comment every time
this skill runs against it, until it's merged or manually labelled
`needs-work` — there's no dedup on repeated runs yet.
