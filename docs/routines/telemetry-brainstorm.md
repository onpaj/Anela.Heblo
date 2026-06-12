# Telemetry Brainstorm Routine

## Overview

A remote Claude Code routine that reads production telemetry from Azure
Application Insights and recent GitHub activity, correlates the two over a
shared window, and files GitHub issues for genuine reliability / performance /
risk signals. It is the telemetry-driven sibling of the
[daily arch-review](./daily-arch-review.md) and
[weekly coverage-gap](./weekly-coverage-gap.md) routines: those read *code*,
this one reads *runtime behaviour*.

It is a **signal** tool, not an alerting tool. Noise (bot scans, expected
auth gating, aborted client fetches) is filtered out deliberately so that the
issues it files are worth a human's attention.

## Routine details

| Field | Value |
|---|---|
| Routine ID | _Not yet created — see "Creating the routine" below_ |
| Schedule | Daily (`0 5 * * *` UTC = 7am Europe/Prague CEST) |
| Model | `claude-sonnet-4-6` |
| Repo | `https://github.com/onpaj/Anela.Heblo` |
| Window | Last 7 days (`P7D`) |

## How it works

1. Runs `scripts/monitoring/brainstorm-telemetry.sh` to gather a deterministic
   Markdown digest of the last 7 days from Application Insights (volume &
   failure-rate trend, result-code mix, 5xx by endpoint, 401/403 hotspots,
   exceptions by type/problemId, browser exceptions, slow & failing
   dependencies, traffic shape).
2. Pulls GitHub context for the same window via the GitHub REST API
   (`scripts/monitoring/gh-api.sh`, auth from `GIT_PAT` — no MCP dependency) —
   recent commits, merged PRs, open issues — to tell **new** problems apart from
   known/already-fixed ones and to attribute signals to recent changes.
3. Correlates: a spike that lines up with a merge is a regression lead; a 500
   whose stack trace names a repository method is a concrete bug; a dependency
   whose p95 is climbing is a capacity/timeout risk.
4. **Deduplicates** each surviving signal against existing GitHub issues
   (see "Deduplication & suppression" below) and drops any that are already
   tracked or were previously dismissed.
5. Files a detailed GitHub issue for each **new** anomaly — typically 0–5 per
   run. Zero is a valid, common outcome.

The routine never makes code changes, never opens PRs, never commits.

## What it flags (file an issue)

- **Server faults** — any sustained 5xx, especially 500s whose exception stack
  names a specific handler/repository (e.g. a `GET Articles/FeedbackList` 500
  tracing to `ArticleRepository.GetFeedbackStatsAsync`).
- **New or rising exception types** — particularly app-owned namespaces
  (`Anela.Heblo.*`) and infrastructure faults that affect users (Npgsql
  connection drops/timeouts, Shoptet/FlexiBee/Graph client failures).
- **Regression leads** — a failure-rate or latency step-change that lines up
  with a recent merge.
- **Latency risk** — a dependency whose p95/p99 is high or trending up against
  a meaningful call volume (e.g. FlexiBee `petra-tesarikova.flexibee.eu` p95 in
  the seconds; outbound LLM calls are expected-slow, not a finding).
- **Permission misconfiguration** — an authenticated endpoint returning 403 at
  high volume to real users (distinct from a user simply lacking a feature).
- **Real frontend errors** — browser exceptions with an app stack
  (`TypeError: ... is not a function`), not third-party/extension noise.

## What it skips (do not file)

- **Bot / scanner traffic** — `POST /wp-login.php`, probes for non-existent
  paths, 405s on unsupported verbs.
- **Expected auth gating** — 401 on polling endpoints (`/api/auth/me`,
  `/api/dashboard/*`) from unauthenticated/expired sessions is normal SPA
  behaviour.
- **Aborted client fetches** — `Fetch` dependencies to `heblo.anela.cz` with
  `resultCode 0` are browser requests cancelled by tab-close or SPA navigation;
  the giant p99 outliers are hung-then-abandoned fetches, not server latency.
- **Expected-slow outbound calls** — `api.anthropic.com` / `api.openai.com`
  LLM dependencies; flag only failures, not duration.
- **Anything already addressed** — a signal whose fix merged inside the window
  (cross-check GitHub before filing).

## Deduplication & suppression

Never file a duplicate, and never re-file something the user already rejected.
Before opening an issue for a surviving signal, the routine **must** check it
against existing GitHub issues and drop it if any of these hold:

1. **A similar issue is open.** An open `telemetry` issue describing the same
   anomaly → skip (it is already tracked). Do not post a "still happening"
   comment unless the signal has materially worsened.
2. **A similar issue was closed by the user without a fix.** A closed issue for
   the same anomaly whose closure did **not** land a fix — i.e. closed as *not
   planned* (`state_reason: not_planned`), or closed manually with **no linked
   merged PR** — is an explicit "won't do" decision. Skip it permanently;
   respect the rejection.

A closed issue that **was** resolved by a merged PR is *not* a suppression: if
the same anomaly reappears after its fix shipped, that is a regression worth a
**new** issue (reference the old one).

### Signal fingerprint (how "similar" is decided)

Prose matching is unreliable, so every filed issue carries a stable,
machine-matchable fingerprint on its own line in the body:

```
telemetry-signal: <category>:<subject>[:<detail>]
```

The fingerprint is derived only from *what* the anomaly is, never from the
counts/percentiles of a particular run, so the same anomaly always produces the
same key. Categories and subjects:

| Category | Subject : detail | Example |
|---|---|---|
| `req-5xx` | `<endpoint>` : `<resultCode>` | `req-5xx:Articles/FeedbackList:500` |
| `req-403` | `<endpoint>` | `req-403:GET /api/StockUpOperations/summary` |
| `exception` | `<type>@<innermost app frame>` | `exception:InvalidOperationException@ArticleRepository.GetFeedbackStatsAsync` |
| `dep-fail` | `<type>:<target>` | `dep-fail:HTTP:homeassistant.tail0cdb23.ts.net` |
| `dep-latency` | `<type>:<target>` | `dep-latency:HTTP:petra-tesarikova.flexibee.eu` |
| `frontend` | `<error>@<symbol>` | `frontend:TypeError-r.filter@Yq1` |

To dedup, search issues (open **and** closed) for the exact `telemetry-signal:`
line:

```bash
scripts/monitoring/gh-api.sh find-signal 'req-5xx:Articles/FeedbackList:500'
```

This returns every matching issue with its `state` and `state_reason`. Match on
the fingerprint, then apply the two rules above — for the "closed without a
merged PR" check, inspect the issue's timeline
(`gh-api.sh GET /repos/onpaj/Anela.Heblo/issues/<n>/timeline`) for a
`closed` event with a `commit_id`/linked merged PR. If no fingerprint match
exists, fall back to a prose comparison of the endpoint/exception before filing.

## Output

Issues are labelled `telemetry` + a severity-ish secondary label
(`reliability`, `performance`, `risk`, or `frontend`). Each issue includes:

- the `telemetry-signal:` fingerprint line (for future dedup),
- the observed signal with **numbers** (counts, percentiles, time window),
- the exception/stack or endpoint it maps to,
- a hypothesis correlating it to recent commits/PRs where one exists,
- a concrete, minimal next step (the file/method to look at, or the query to
  re-run).

Find all open telemetry issues:
```
https://github.com/onpaj/Anela.Heblo/issues?q=label%3Atelemetry+is%3Aopen
```

## Environment dependency

Unlike the code-reading routines, this one needs **outbound network + secrets**,
which are configured on the routine's environment (not in the repo):

| Requirement | Value |
|---|---|
| Egress allowlist (Custom) | `api.applicationinsights.io` (not in the default Trusted list) |
| Egress | `api.github.com` (issue search + create, via `gh-api.sh`) |
| Env secret | `APPINSIGHTS_APP_ID` = `53f2124c-ca25-42bf-907c-17b02df8d343` |
| Env secret | `APPINSIGHTS_API_KEY` = rotated read-telemetry key (never in repo) |
| Env secret | `GIT_PAT` = token with `repo` scope (Issues read/write) |

The App Insights key grants **read-only telemetry** access; `GIT_PAT` is used by
`gh-api.sh` to search and create issues **instead of the GitHub MCP server**, so
the routine has no MCP dependency. No secret is stored in the repository; the
scripts read all values from the environment. See
`docs/handoff/appinsights-brainstorm-routine.md` for the full config reference.

### Labels (one-time setup)

The routine's labels must exist in the repo. `telemetry`, `reliability`,
`performance`, `risk`, and `frontend` are not present yet — create them once
(the GitHub API would otherwise auto-create them with a default grey colour):

```bash
for l in "telemetry:0e8a16" "reliability:b60205" "performance:fbca04" \
         "risk:d93f0b" "frontend:1d76db"; do
  name="${l%%:*}"; color="${l##*:}"
  scripts/monitoring/gh-api.sh POST /repos/onpaj/Anela.Heblo/labels \
    "$(jq -n --arg n "$name" --arg c "$color" '{name:$n, color:$c}')"
done
```

> Environment changes (egress + secrets) on Claude Code for web apply at
> **container creation**, not to a running session. After editing the
> environment, a new session/run is required for them to take effect.

## Creating the routine

The routine does not exist yet. Create it from the Claude Code web UI (or ask
Claude Code to create it) against the environment that already has the egress +
secrets above, with the schedule `0 5 * * *`, model `claude-sonnet-4-6`, and
this prompt:

```
You are the daily telemetry-anomaly routine for the Anela Heblo production app.
Your job: find anomalies/issues in Application Insights and open a detailed
GitHub issue for each NEW one — after rigorously deduplicating.

1. Run: ./scripts/monitoring/brainstorm-telemetry.sh   (default window is the last 7 days)
   If the first line of output is an error about egress or APPINSIGHTS_*, stop and
   report that the environment is missing network access or secrets — do not guess.
   Re-run with a custom --timespan or use ./scripts/monitoring/appinsights-query.sh
   '<KQL>' to drill into anything ambiguous.

2. Pull GitHub context for the same 7-day window via scripts/monitoring/gh-api.sh
   (GitHub REST API, auth from GIT_PAT — do NOT use the GitHub MCP server):
     ./scripts/monitoring/gh-api.sh GET '/repos/onpaj/Anela.Heblo/commits?since=<ISO>'
     ./scripts/monitoring/gh-api.sh GET '/repos/onpaj/Anela.Heblo/pulls?state=all&per_page=30'
   to see recent commits, merged PRs, and open issues.

3. Read docs/routines/telemetry-brainstorm.md and apply its "What it flags" /
   "What it skips" rules exactly. In particular: ignore bot traffic, expected
   401 auth gating, and resultCode-0 aborted browser fetches.

4. For EACH surviving anomaly, compute its `telemetry-signal:` fingerprint (see
   the doc's table) and search existing issues — open AND closed — for that exact
   fingerprint line:
     ./scripts/monitoring/gh-api.sh find-signal '<fingerprint>'
   Then:
     - matching OPEN issue                          -> SKIP (already tracked)
     - matching issue CLOSED without a fix          -> SKIP (user rejected it:
       state_reason "not_planned", or closed with no linked merged PR)
     - matching issue CLOSED by a merged PR, but the
       anomaly is back                              -> file a NEW issue, ref the old
     - no match                                     -> file a new issue
   When in doubt about whether two findings are "the same", err toward SKIP and
   leave a note rather than risk a duplicate.

5. File a detailed GitHub issue for each anomaly that passes step 4 with
   gh-api.sh create-issue, labelled `telemetry` plus one of
   `reliability` / `performance` / `risk` / `frontend`:
     printf '%s' "$body" | ./scripts/monitoring/gh-api.sh create-issue \
       "<title>" "telemetry,reliability" -
   Each issue MUST contain, as its first body line, the `telemetry-signal:`
   fingerprint, then: concrete numbers (counts, percentiles, window), the mapped
   exception/endpoint, a correlation hypothesis where one exists, and a minimal
   next step. Typically 0–5 issues; zero is fine.

Never change code, never open a PR, never commit.
```

## Managing the routine

Once created, manage it (pause / enable / delete / update prompt) from its
Web UI page, or ask Claude Code with the routine ID. Record the assigned
`trig_…` ID in the table at the top of this doc.

## Triage

Review open `telemetry` issues periodically. A signal that recurs across several
runs without a fix is a standing risk worth scheduling; one that stops
appearing after a merge can be closed as resolved.
