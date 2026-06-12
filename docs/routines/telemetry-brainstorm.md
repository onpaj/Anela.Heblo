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
2. Pulls GitHub context for the same window via MCP — recent commits, merged
   PRs, open issues — to tell **new** problems apart from known/already-fixed
   ones and to attribute signals to recent changes.
3. Correlates: a spike that lines up with a merge is a regression lead; a 500
   whose stack trace names a repository method is a concrete bug; a dependency
   whose p95 is climbing is a capacity/timeout risk.
4. Files 0–5 GitHub issues for the signals that survive the noise filter. Zero
   is a valid, common outcome.

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

## Output

Issues are labelled `telemetry` + a severity-ish secondary label
(`reliability`, `performance`, `risk`, or `frontend`). Each issue includes:

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
| Env secret | `APPINSIGHTS_APP_ID` = `53f2124c-ca25-42bf-907c-17b02df8d343` |
| Env secret | `APPINSIGHTS_API_KEY` = rotated read-telemetry key (never in repo) |

The API key grants **read-only telemetry** access. No secret is stored in the
repository; the scripts read both values from the environment. See
`docs/handoff/appinsights-brainstorm-routine.md` for the full config reference.

> Environment changes (egress + secrets) on Claude Code for web apply at
> **container creation**, not to a running session. After editing the
> environment, a new session/run is required for them to take effect.

## Creating the routine

The routine does not exist yet. Create it from the Claude Code web UI (or ask
Claude Code to create it) against the environment that already has the egress +
secrets above, with the schedule `0 5 * * *`, model `claude-sonnet-4-6`, and
this prompt:

```
You are the daily telemetry-brainstorm routine for the Anela Heblo production app.

1. Run: ./scripts/monitoring/brainstorm-telemetry.sh   (default window is the last 7 days)
   If the first line of output is an error about egress or APPINSIGHTS_*, stop and
   report that the environment is missing network access or secrets — do not guess.

2. Pull GitHub context for the same 7-day window via the MCP tools (scoped to
   onpaj/anela.heblo): recent commits, merged PRs, and open issues.

3. Read docs/routines/telemetry-brainstorm.md and apply its "What it flags" /
   "What it skips" rules exactly. In particular: ignore bot traffic, expected
   401 auth gating, and resultCode-0 aborted browser fetches; do not re-file a
   signal whose fix merged inside the window.

4. File 0–5 GitHub issues for the surviving signals, labelled `telemetry` plus
   one of `reliability` / `performance` / `risk` / `frontend`. Each issue must
   cite concrete numbers, the mapped exception/endpoint, a correlation
   hypothesis where one exists, and a minimal next step. Zero issues is fine.

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
