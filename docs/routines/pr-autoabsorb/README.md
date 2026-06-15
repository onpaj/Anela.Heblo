# PR Auto-Absorb Routine

A remote Claude Code routine that finds the oldest **agent-authored** PR that is
**broken** — merge-conflicting against `main` or with a failed CI build — and
runs the [`/absorb`](../../../.claude/skills/absorb/SKILL.md) skill on it:
backmerge `main`, resolve conflicts, run the test suite, fix what's broken, and
push the reconciled branch back to the existing PR.

Unlike the other three routines (`telemetry-anomaly`, `daily-arch-review`,
`weekly-coverage-gap`), which are **read-only signal tools that only file
issues**, this one **writes code and pushes**. It is an *active fixer*, so its
scope is deliberately narrow and its guardrails are strict (see below).

## Files in this folder

| File | Purpose |
|---|---|
| `pr-scan.sh` | Deterministic selector: lists open PRs via `gh`, keeps agent/bot PRs that are conflicting or have a failed CI check, drops `absorb-blocked` ones, prints the **oldest** such PR number (or nothing). `--all` prints a debug table. |
| `README.md` | This file — the routine definition and the prompt to paste into the Claude Code web UI. |

The conflict-resolution / test-fixing / push logic is **not** reimplemented
here — it is the existing `/absorb` skill (`.claude/skills/absorb/SKILL.md`).
This routine only *selects* the PR and *invokes* the skill under unattended
guardrails.

## Routine details

| Field | Value |
|---|---|
| Routine ID | _Not yet created — see "Creating the routine" below_ |
| Schedule | Daily (`0 6 * * *` UTC = 8am Europe/Prague CEST) — _proposed_ |
| Model | `claude-sonnet-4-6` (conflict + test-fix reasoning may warrant a stronger model — see "Model") |
| Repo | `https://github.com/onpaj/Anela.Heblo` |
| Base branch | `main` |
| Per-run scope | **One PR per run** (oldest eligible) |

## Scope — which PRs it may touch

Only **agent/bot-authored** PRs, identified by either signal:

- head branch starts with **`claude/`** (every PR opened from the Claude Code
  web UI), **or**
- the PR carries the **`agent`** label.

Human-authored PRs are never touched — the routine must not auto-resolve
conflicts or rewrite a hand-crafted branch. Draft PRs and any PR labelled
`absorb-blocked` are also skipped.

## How it works

1. Run `docs/routines/pr-autoabsorb/pr-scan.sh`. It returns **one** PR number —
   the oldest open, non-draft, agent-authored PR that is `CONFLICTING` against
   `main` **or** has a failing CI check, excluding any labelled
   `absorb-blocked`. **No output ⇒ nothing to do** (a valid, common outcome —
   stop).
2. Run `/absorb <number>`. The skill checks out the PR branch, backmerges
   `origin/main`, resolves conflicts, runs the full suite
   (`dotnet test`; `npm run build && npm run lint`), fixes failing tests, and
   **pushes to the existing PR branch** — updating the PR in place. It never
   opens a new PR.
3. On success, the PR is updated; the routine stops (one PR per run).

## Unattended guardrails (this routine ≠ an interactive `/absorb`)

`/absorb` is written to **pause and ask the user** on ambiguous conflicts or
hard test failures. A scheduled run has no user, so the routine must convert
every "ask the user" into a **safe stop**, never a guess:

- **Ambiguous conflict / unfixable test** — do **not** push a partial or guessed
  resolution. Instead: `git merge --abort` (or reset to the remote head so the
  branch is left untouched), post a PR comment describing exactly what's
  blocked, add the **`absorb-blocked`** label so future runs skip it, and stop.
- **Push rejected** (someone advanced the branch mid-run) — abort, comment, do
  **not** force-push. Never `--force`.
- **Already green** — if the chosen PR turns out to need no backmerge and is
  passing, do nothing and stop (the scan shouldn't pick it, but double-check).
- **Repeat offender** — if the PR already carries a recent routine
  `chore: backmerge …` / `fix: …` commit and is *still* failing for the same
  reason, don't re-absorb in a loop: label it `absorb-blocked`, comment, stop.
- **Out of scope** — never act on a PR whose branch isn't `claude/*` and which
  lacks the `agent` label, even if `/absorb <n>` is invoked manually.

The routine touches only the one selected PR's branch; it never commits to
`main`, never opens PRs, never deletes branches.

## Model

`claude-sonnet-4-6` for consistency with the other routines and cost. Note that
this routine's core work — reasoning about merge conflicts in domain code and
fixing failing tests — is meaningfully harder than the issue-filing routines, so
a stronger model (e.g. an Opus tier) is a reasonable upgrade if absorb quality
proves marginal. Start on Sonnet; revisit after a few live runs.

## Environment dependency

This is the heaviest routine environment — it needs the **full build toolchain
and write access**, not just read APIs:

| Requirement | Why |
|---|---|
| **`gh` CLI installed + authenticated** | Both `pr-scan.sh` and `/absorb` step 2 call `gh`. Install it in the environment's setup script; auth from `GH_TOKEN` (or `GIT_PAT`, which `pr-scan.sh` maps to `GH_TOKEN`). |
| **Git push credentials** (`GIT_PAT`, `repo` scope) | `/absorb` pushes the reconciled branch. |
| **Git author identity** configured | The backmerge / fix commits need `user.name` / `user.email`. |
| **.NET 8 SDK + Node toolchain** | `/absorb` runs `dotnet test` and `npm run build && npm run lint` to verify and fix the branch. |
| **Egress** to `github.com` / `api.github.com` + the package registries the install and `dotnet`/`npm` restore need | PR ops + dependency restore at build time. |

> Environment changes (installed tools, egress, secrets) on Claude Code for web
> apply at **container creation**, not to a running session. A new run is
> required after editing the environment.

### Label (one-time setup)

The routine uses an **`absorb-blocked`** label to permanently skip PRs it can't
safely fix unattended. The `agent` label already exists. Create the new one once:

```bash
gh label create absorb-blocked --color d93f0b \
  --description "PR /absorb could not safely reconcile unattended" \
  --repo onpaj/Anela.Heblo
```

## Creating the routine

The scheduled routine does not exist yet. Create it from the Claude Code web UI
against an environment that has the toolchain + secrets above, with schedule
`0 6 * * *`, model `claude-sonnet-4-6`, and **this prompt**:

```
You are the PR auto-absorb routine for Anela Heblo. Your job: take the single
oldest BROKEN agent-authored PR and reconcile it via the /absorb skill — under
strict unattended guardrails. Read docs/routines/pr-autoabsorb/README.md first;
it defines the scope, the guardrails, and the safe-stop rules you MUST follow.

1. Run: ./docs/routines/pr-autoabsorb/pr-scan.sh
   - It prints ONE PR number (oldest open, non-draft, agent-authored —
     branch claude/* OR label "agent" — that is CONFLICTING against main OR has a
     failed CI check; absorb-blocked PRs excluded).
   - If it prints NOTHING, there is no work: stop. (Common, valid outcome.)
   - If it errors about `gh` or auth, stop and report the environment is missing
     the gh CLI / token — do not guess.

2. Run /absorb <number> for that PR. It will backmerge origin/main, resolve
   conflicts, run the suite (dotnet test; npm run build && npm run lint), fix
   failing tests, and push to the existing PR branch. Do NOT open a new PR.

3. Convert every "ask the user" in /absorb into a SAFE STOP — never a guess:
   - Ambiguous conflict or test you can't confidently fix -> abort the merge /
     reset so the branch is left untouched, comment on the PR describing exactly
     what's blocked, add the label "absorb-blocked", and stop.
   - Push rejected (branch advanced mid-run) -> abort, comment, NEVER force-push.
   - PR already green / needs nothing -> do nothing, stop.
   - PR already has a recent routine backmerge/fix commit and is STILL failing
     the same way -> don't loop: label "absorb-blocked", comment, stop.
   - Never act on a PR outside scope (not claude/* and no "agent" label).

4. One PR per run. Never commit to main, never open a PR, never force-push,
   never delete branches. On success, optionally leave a one-line summary
   comment on the PR.
```

After creating it, record the assigned `trig_…` ID in the "Routine details"
table above.

## Managing the routine

Once created, manage it (pause / enable / delete / update prompt) from its Web
UI page, or ask Claude Code with the routine ID.

## Triage

Review PRs labelled `absorb-blocked` periodically — each is a PR the routine
couldn't safely reconcile unattended and a human needs to resolve. Once
resolved, remove the label to let the routine consider it again.

```
https://github.com/onpaj/Anela.Heblo/pulls?q=is%3Apr+is%3Aopen+label%3Aabsorb-blocked
```
