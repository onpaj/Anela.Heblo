---
name: automerge-all
description: Review every open agent-created PR with a fresh automerge-pr run each and autonomously squash-merge the high-confidence ones. Use when the user says "automerge-all", "automerge", "merge ready PRs", "review open PRs", or asks to clear the PR backlog without reviewing each one by hand.
---

You autonomously clear the pipeline's PR backlog by running `/automerge-pr`
against every open candidate, reviews in parallel, merges applied one at a
time so two merges never race on `master`.

## 1. Find the candidates

```bash
.claude/skills/automerge-pr/candidates.sh
```

If `candidates` is empty, print `No agent PRs ready to review.`, list
`skipped` with reasons, and stop.

## 2. Review each candidate — one subagent per PR, in parallel, orchestrated mode

Spawn **one subagent per candidate PR, all in a single message**, so they
run concurrently — fresh context per PR. Give each subagent exactly this
prompt, with `{N}` replaced by the PR number:

> Follow `.claude/skills/automerge-pr/SKILL.md` for PR #{N} in this
> repository. Skip its step 1 (you already have the PR number: {N}).
>
> **ORCHESTRATED MODE**: when you reach step 5 (apply the verdict), do NOT
> call `apply_verdict.sh` for a review verdict. Instead write the parsed
> verdict JSON from step 4 to `/tmp/automerge-verdict-{N}.json` and end your
> entire final message with exactly one line: `VERDICT_FILE:
> /tmp/automerge-verdict-{N}.json`.
>
> This does NOT apply to step 2's hygiene check — if step 2 reports
> `still-failing` or `conflict`, the script itself already flagged the PR
> `needs-work` and posted the reason (nothing for you to do there); just
> follow step 2 as written and, instead of continuing to step 3, end your
> final message with exactly one line:
> `HYGIENE_REJECTED: {status} — {detail}`.
>
> If step 2 reports `ci-running`, end your final message with exactly one
> line: `SKIPPED: CI already running from a prior push, retry later`.
> Nothing was touched this run — no branch update, no polling.
>
> If step 2 reports `pending-timeout`, end your final message with exactly
> one line: `SKIPPED: CI checks pending, retry later`.
>
> If step 2 reports `error`, end your final message with exactly one line:
> `SKIPPED: hygiene check errored, retry later — {detail}`. Do NOT
> auto-reject on `error`: it is an API/infrastructure failure, not a
> verdict about the PR.

## 3. Collect results

For each subagent's final line:

- `VERDICT_FILE: {path}` → read the JSON at that path, queue it for step 4.
- `HYGIENE_REJECTED: ...` or `SKIPPED: ...` → record it for the report; no
  further action, this PR is already resolved or intentionally untouched.

## 4. Apply the queued verdicts — serially

Process the queued verdicts one at a time, **in ascending PR number**, so
two merges never race on `master`:

```bash
.claude/skills/automerge-pr/apply_verdict.sh \
  --pr {N} --action {action} --review-file /tmp/automerge-review-{N}.md --issue {issue}
```

(The `review-file` path is whatever the subagent wrote to in its own step
4 — `/tmp/automerge-review-{N}.md` per `automerge-pr/SKILL.md`.) Use the
`linkedIssue` from step 1's candidate object; pass `--issue` when non-null.
A non-zero exit means that PR failed. **Continue to the next PR
regardless** — one failure never aborts the batch.

A merge verdict applied here can come back `"status": "needs-work"`
instead of `"ok"` — this is expected and not a bug in this run: merging PR
N can turn PR N+1 in the same batch from mergeable to conflicting, since
they were both scored against `master` as it stood at review time, before
either merged. `apply_verdict.sh` detects exactly that case itself and
flags the now-conflicting PR `needs-work` with a `verdict: REJECT`
comment — there is nothing further for you to do for that PR; just record
its `detail` for step 5's report like any other outcome.

## 5. Report

Print a table of every PR, one row each, with these columns:

| PR | created | hygiene outcome | score | verdict | action |
|----|---------|-----------------|-------|---------|--------|

`created` is the `createdAt` field from step 1's candidate object — how long
a PR has been sitting is what tells a human whether the backlog is draining
or just churning. (Ordering stays ascending PR number, matching step 4's
serial-apply order; `createdAt` is reported, not sorted on.) `score` is
blank for a PR that never reached review.

Then list:

- `skipped` from step 1, with reasons
- every `HYGIENE_REJECTED` and `SKIPPED` PR from step 3
- any PR whose review was unparseable
- any `apply_verdict.sh` failure, with its `detail`
- if `truncated` > 0, state exactly how many PRs were left unprocessed

The user reads only this report. It must say what was *not* done as clearly
as what was.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MERGE_THRESHOLD`, `NEEDS_WORK_THRESHOLD` | `automerge-pr/parse_verdict.py` |
| `MAX_CANDIDATES`, `AGENT_LABEL` | `automerge-pr/candidates.sh` |
| `MERGED_ISSUE_LABEL`, `NEEDS_WORK_LABEL` | `automerge-pr/apply_verdict.sh` |

## Limits worth knowing

Same limits as `/automerge-pr` (no test-suite execution, prompt-only
READ-ONLY constraint, no confirmation prompt, no dedup on repeated
`comment`-band reviews) — see that skill's *Limits* section, not restated
here.

Verdict application happens serially even though review happens in
parallel — a PR flagged `needs-work` on hygiene grounds during the parallel
phase is already resolved by the time step 4 runs; only real review
verdicts (`merge`/`comment`/`needs-work` from a completed review) go
through the serial queue.
