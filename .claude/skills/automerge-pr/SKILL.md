---
name: automerge-pr
description: Review one PR — bringing it current with main and confirming CI first — and merge it if the review is confident, comment if not, or flag needs-work if it can't even be reviewed. Use when the user says "automerge-pr", "review and merge PR N", "check if PR N is ready to merge", or gives a specific PR number to clear.
---

You take one PR from "open" to a decision: merged, commented and flagged
`human-required`, or flagged `needs-work` — after first making sure it's
actually current with `main` and its CI is green. Called directly for one
PR, or by `/automerge-all` as part of a full-backlog sweep.

**All deterministic work is done by the scripts beside this file.** Do not
re-implement their logic, re-derive the score thresholds, or hand-write
GitHub calls they already own. Your only judgement call is the review
itself.

## GitHub access: MCP for you, scripts for everything else

GitHub access here is split in two, and the split is deliberate:

- **Everything *you* read or write directly** goes through the **`github`
  MCP server** — the `mcp__github__*` tools. Do not shell out to `gh`, and
  do not hand-write `curl` calls against `api.github.com`.
- **Everything the scripts do** (`candidates.sh`, `apply_verdict.sh`,
  `hygiene-pr/update_and_wait.sh`, `hygiene-pr/resolve_conflict.sh`) stays
  inside those scripts. They keep
  their own transport — `gh` by default, or `.claude/skills/_lib/gh_api.sh`
  when `USE_GH_API` is set. That is their business, not yours: never
  reimplement a script's GitHub call as an MCP call to "check its work".

Every `mcp__github__*` call needs `owner` and `repo`. Resolve them once, at
the start of the run, from `GH_REPO` (format `owner/repo`) if it is set,
otherwise from the `origin` remote:

```bash
echo "${GH_REPO:-$(git remote get-url origin)}"
```

Parse `owner` and `repo` out of that and reuse them for every MCP call
below. `git` itself is fine to run — it is not a GitHub API call.

**If the `github` MCP server is not available** in this environment (common
in headless or scheduled runs, where an interactively-authenticated MCP
server may not be connected), stop and report that as the reason rather
than falling back to `gh` yourself. The scripts still work without MCP; the
review step does not.

## 1. Resolve the target PR

If a PR number was given in your invocation, use it as `{N}`. Otherwise,
check whether the branch you're currently on already has an open PR — if
so, treat it as the target, the same as an explicit number:

Get the current branch with `git rev-parse --abbrev-ref HEAD`, then call:

**`mcp__github__list_pull_requests`**
- `owner`, `repo` — as resolved above
- `state`: `"open"`
- `head`: `"{owner}:{current-branch}"`
- `fields`: `["number", "state"]`

If that returns exactly one PR, use its `.number` as `{N}` and skip the
candidate search below. If it returns nothing, find the oldest
(lowest-numbered) open `agent` PR:

```bash
.claude/skills/automerge-pr/candidates.sh
```

Take the lowest `.number` from `.candidates`. If `candidates` is empty,
print `No agent PRs ready to review.`, list `skipped` with reasons, and
stop.

## 2. Bring it current and confirm CI — always call `hygiene-pr`

```bash
.claude/skills/hygiene-pr/update_and_wait.sh --pr {N}
```

This single call is cheap when there's nothing to do: if the PR is
mergeable and its checks are green, it reports `already-clean` immediately
with no `gh pr update-branch` call and no polling — including when the
branch is some commits behind `master`, which blocks nothing and so is not
back-merged. There is no separate "is it already fine" check to do
yourself — this call *is* that check, plus the fix, in one step.

Branch on its `status`:

- **`already-clean` or `fixed`** → continue to step 3 (review).
- **`still-failing`** → **skip the review entirely.** The script itself
  already flagged this PR `needs-work` and posted a comment explaining why
  (via `_lib/flag_needs_work.sh` → `apply_verdict.sh`, the same mechanism
  this skill uses for a code-review rejection) — that happens inside
  `update_and_wait.sh` now, not here, so hygiene-pr/hygiene-all produce the
  same durable trail even when run standalone. Do not post anything
  yourself. Report this PR (number, hygiene status, action: `needs-work`)
  and stop — do not proceed to step 3 for this PR.
- **`conflict`** → nothing has been flagged: resolving the conflict is
  hygiene's job, and it has not been attempted yet. Follow
  **`.claude/skills/hygiene-pr/SKILL.md` step 3** for this PR — it is the
  one owner of that procedure; do not improvise your own `git merge`.
  Then:
  - it reported `conflict-resolved` → re-read the follow-up
    `update_and_wait.sh --force` status it gives you and branch on it here
    exactly as if it had come from step 2 (`already-clean`/`fixed` →
    continue to step 3; anything else → its own bullet above).
  - it reported `conflict-unresolved` → the PR is already flagged
    `needs-work`. Report it (number, `conflict-unresolved`, action:
    `needs-work`) and stop.
  - it reported `conflict-claimed-elsewhere` or `conflict-not-open` →
    report this PR as skipped with that reason and stop. Nothing was
    touched and nothing is flagged.
- **`ci-running`** → report this PR as skipped (`CI already running from a
  prior push, retry later`) and stop. Nothing was touched this run — no
  branch update, no polling: the PR needs nothing done to it except for a
  build someone else started to finish, and this run won't spend its poll
  window waiting on that.
- **`pending-timeout`** → report this PR as skipped
  (`CI checks pending, retry later`) and stop.
- **`error`** → report this PR as skipped
  (`hygiene check errored, retry later` plus the `detail` verbatim) and
  stop. Treat it exactly like `pending-timeout`: **do not auto-reject.**
  An `error` means the GitHub API call itself failed (auth, rate limit,
  network) — it is an infrastructure failure, not a judgement about this
  PR, and labelling it `needs-work` would burn a revision attempt for
  something the PR did not do.

## 3. Review

Spawn **one `code-reviewer` subagent** with exactly this prompt, `{N}`
replaced by the PR number:

> Review pull request #{N} in this repository and score how confident you are
> that it can be merged to `master` with no human looking at it.
>
> All GitHub access goes through the `github` MCP server (`mcp__github__*`
> tools) against `owner: {owner}`, `repo: {repo}`. Do not shell out to the
> `gh` CLI and do not `curl` `api.github.com`.
>
> You are READ-ONLY. You must not call `mcp__github__merge_pull_request`,
> `mcp__github__update_pull_request`, `mcp__github__issue_write`,
> `mcp__github__add_issue_comment`, `mcp__github__pull_request_review_write`,
> or any other write tool, and you must not run `git push` or any other
> state-changing command. Gather context with:
> - `mcp__github__pull_request_read` with `method: "get"`, `pullNumber: {N}`
>   — title, body, head ref, `additions`, `deletions`, `changed_files`, and
>   the author under `.user.login`
> - `mcp__github__pull_request_read` with `method: "get_files"`,
>   `pullNumber: {N}` — the changed-file list; each entry's path is its
>   `filename`
> - `mcp__github__pull_request_read` with `method: "get_diff"`,
>   `pullNumber: {N}` — the diff itself
> - `mcp__github__issue_read` with `method: "get"`, `issue_number: <issue>`
>   for the issue the PR body links
> - `Read` and `Grep` on the repo, to check the change fits the code around it
>
> Do not run the test suite, or any individual test, yourself — under any
> circumstances, via Bash or any other tool. Whether tests pass is CI's
> determination, not yours, and it's already been made (step 2 confirms CI
> is green before you're ever invoked). Your job re: tests is narrower:
> confirm the relevant behavior is *covered* by one, not to execute it or
> independently re-confirm its outcome. You also must not assume the code
> works just because it looks plausible.
>
> First check `.user.login` from the `method: "get"` call and every entry's
> `filename` from the `method: "get_files"` call above:
>
> - **Dependency-bot PR** — author is a known dependency-update bot
>   (`dependabot[bot]`, `renovate[bot]`, or similar — also recognizable from
>   a `dependabot/...` or `renovate/...` head branch). This is an automated
>   version bump, not scoped feature work: it has no linked issue by design.
>   Skip the "no linked issue" and "diff does something the linked issue did
>   not ask for" deductions below, and instead judge whether the diff is a
>   plausible, scoped bump (dependency manifest/lockfile — or a pinned
>   action version in a workflow file — changed consistently with the
>   version bump the title describes, nothing unrelated bundled in).
> - **Documentation-only PR** — every changed file is non-code
>   documentation (paths under `docs/`, `*.md`, `*.txt`, `README*`,
>   `LICENSE*`, or similar prose — no source, config, script, or workflow
>   file touched). A docs-only change doesn't need a tracked issue either.
>   Skip the same two deductions, and instead judge whether the prose is
>   accurate against the current code (`Read`/`Grep` the paths it
>   describes) and doesn't contradict other docs.
>
> Both exemptions only waive those two specific deductions — every other
> deduction below still applies as written, including the workflow-file and
> cannot-verify-correctness ones.
>
> For every other PR — and for every remaining deduction on an exempted
> one — start from 100 and deduct:
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
> It does not, however, cover behaviour whose only gap is that you didn't
> personally run a test for it — you're not supposed to. That includes
> container/integration tests: they ran in CI, not your sandbox, and a real
> failure there would already have flagged this PR `needs-work` before it
> ever reached you, so a green CI run is the verification, regardless of
> what kind of test produced it. Reserve this deduction for logic that no
> test covers at all (the "no accompanying test" deduction above already
> catches that) and that you also can't reason through by reading the diff
> — never for "I can't personally run this to check."
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

## 4. Parse the verdict

Write the subagent's full output to `/tmp/automerge-review-{N}.md` using the
**Write tool** — never interpolate a subagent's output into a shell command.
Then:

```bash
.claude/skills/automerge-pr/parse_verdict.py < /tmp/automerge-review-{N}.md
```

A malformed or self-contradictory review comes back `"valid": false` with
`"action": "comment"` — that is correct and final. **Never override it into
a merge.**

## 5. Apply the verdict

Determine your mode from your invocation prompt:

- **Standalone** (no explicit instruction to withhold the merge): apply
  immediately.

  ```bash
  .claude/skills/automerge-pr/apply_verdict.sh \
    --pr {N} --action {action} --review-file /tmp/automerge-review-{N}.md --issue {issue}
  ```

  Use the `linkedIssue` field from step 1's candidate object (or the
  `linkedIssue` the caller gave you if invoked with an explicit PR number
  and no candidates.sh lookup was needed — if you don't already have it,
  fetch the PR body with `mcp__github__pull_request_read`
  (`method: "get"`, `pullNumber: {N}`) and apply the same `Closes #(\d+)`
  pattern `candidates.sh` uses). Pass `--issue` when non-null, omit it when
  null.

- **Orchestrated** (your invocation explicitly says "ORCHESTRATED MODE" —
  this is how `/automerge-all` calls you): do **not** call
  `apply_verdict.sh`. Instead write the parsed verdict JSON from step 4 to
  the path your invocation specifies (or `/tmp/automerge-verdict-{N}.json`
  if none given) using the **Write tool**, and end your entire output with
  exactly one line: `VERDICT_FILE: {that path}`.

## 6. Report

State: PR number, hygiene outcome (`none` if step 2 wasn't reached because
you took an explicit-PR-number fast path — this should not happen, step 2
always runs — otherwise the `status` from step 2), score, verdict, action
taken (or `deferred to caller` in orchestrated mode).

`apply_verdict.sh --action merge` can itself report `"status": "needs-work"`
instead of `"ok"` — if the merge fails because the PR became unmergeable
between review and merge (most commonly: an earlier PR in the same
`/automerge-all` batch just merged and moved the default branch underneath
this one), the script flags it `needs-work` with a `verdict: REJECT`
comment itself, the same durable trail a hygiene or code-review rejection
leaves, so it's discoverable by `/rework-pr` and counts toward its
revision-attempt cap. Nothing further for you to do — report it as you
would any other `needs-work` outcome.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MERGE_THRESHOLD`, `NEEDS_WORK_THRESHOLD` | `parse_verdict.py` |
| `MAX_CANDIDATES`, `AGENT_LABEL` | `candidates.sh` |
| `MERGED_ISSUE_LABEL`, `NEEDS_WORK_LABEL`, `HUMAN_REQUIRED_LABEL` | `apply_verdict.sh` |
| `HYGIENE_POLL_INTERVAL_SECONDS`, `HYGIENE_POLL_MAX_ATTEMPTS`, `HYGIENE_NO_CHECKS_GRACE_ATTEMPTS` | `hygiene-pr/update_and_wait.sh` |
| the needs-work comment block (`verdict: REJECT`) | `_lib/flag_needs_work.sh` |

## Limits worth knowing

This skill merges without running the test suite beyond whatever CI already
ran — every score comes from reading a diff, and CI passing only means
GitHub's own checks were green, not that this skill re-verified them
locally. It is deliberately conservative (a high merge threshold defined
once in `parse_verdict.py`, uncertainty costs score), but it can merge a
change that reads correctly and is not. There is also no confirmation
prompt. Watch the first few runs.

The reviewer subagent's READ-ONLY instruction is a prompt constraint, not an
enforced sandbox — it currently has Bash access and the same `github` MCP
credentials as this skill, which include the write tools it is told not to
call. A subagent that follows a malicious instruction embedded in a PR's
diff or title could act independently of the score it reports. Until a
reviewer ships that is restricted to the read-only `mcp__github__*` tools
(or has no Bash at all), treat every merge this skill performs as something
a compromised or confused subagent could have influenced beyond its stated
score.

A PR that lands in the `comment` band gets the review posted once, then is
labelled `human-required` and excluded from future candidate lists by
`candidates.sh` — it will not be re-reviewed on later sweeps. A human must
either act on it directly (merge, close, push a fix) or remove the label
to send it through another automated pass.

A PR whose conflict step 2 resolved is reviewed on the diff *including* that
resolution — the merge commit this run just pushed is part of what the
reviewer scores, and a wrong reconciliation can therefore be merged by this
skill on the strength of a review that had no independent view of the
conflicting hunks. `/rework-pr` also resolves conflicts, as part of revising
a rejected PR; the two are different entry points to the same judgement
call, not a fallback chain.
