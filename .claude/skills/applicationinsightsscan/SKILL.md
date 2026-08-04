---
name: applicationinsightsscan
description: Daily/on-demand telemetry-anomaly scan for this repository's production app. Reads Azure Application Insights telemetry for the app this repo ships, correlates it with recent commits/PRs/issues in this repo (target GitHub repo auto-detected from the git 'origin' remote, override via GH_REPO), rigorously deduplicates against existing GitHub issues, and files a detailed issue for each genuinely new anomaly. Use when the user says "telemetry scan", "check app insights", "applicationinsightsscan", "run telemetry anomaly routine", "scan telemetry", or asks for a production reliability/anomaly audit against Application Insights. Requires APPINSIGHTS_APP_ID / APPINSIGHTS_API_KEY / GIT_PAT env vars and network egress to api.applicationinsights.io + api.github.com — see "Required environment" below. Read-only against code — never modifies code, opens a PR, or commits.
---

You are the telemetry-anomaly routine for this repository's production app.
Your job: find anomalies/issues in Application Insights and open a detailed
GitHub issue for each NEW one — after rigorously deduplicating. Read
`.claude/skills/applicationinsightsscan/telemetry-rules.md` first; it defines
the flag/skip rules, the telemetry-signal fingerprint scheme, and the dedup
rules you must follow exactly.

The target GitHub repo is whatever this checkout's `origin` remote points at
(auto-detected by `gh-api.sh`; override with `GH_REPO=owner/repo` if needed).
Get it once at the start with:
```
REPO="$(.claude/skills/applicationinsightsscan/gh-api.sh repo)"
```

1. Run: `.claude/skills/applicationinsightsscan/telemetry-digest.sh` (default
   window = last 7 days). If the first line of output is an error about egress
   or `APPINSIGHTS_*`, stop and report that the environment is missing network
   access or secrets — do not guess. Drill into anything ambiguous with:
   ```
   .claude/skills/applicationinsightsscan/appinsights-query.sh '<KQL>'   # add --timespan P7D as needed
   ```

2. Pull GitHub context for the same 7-day window via
   `.claude/skills/applicationinsightsscan/gh-api.sh` (GitHub REST API, auth
   from `GIT_PAT` — do NOT use the GitHub MCP server):
   ```
   .claude/skills/applicationinsightsscan/gh-api.sh GET "/repos/${REPO}/commits?since=<ISO>"
   .claude/skills/applicationinsightsscan/gh-api.sh GET "/repos/${REPO}/pulls?state=all&per_page=30"
   ```
   to see recent commits, merged PRs, and open issues.

3. Apply `telemetry-rules.md`'s "What it flags" / "What it skips" rules
   exactly. In particular: ignore bot traffic, expected 401 auth gating, and
   resultCode-0 aborted browser fetches; do not re-file a signal whose fix
   merged in-window.

4. For EACH surviving anomaly, compute its `telemetry-signal:` fingerprint
   (see the `telemetry-rules.md` table) and search existing issues — open AND
   closed:
   ```
   .claude/skills/applicationinsightsscan/gh-api.sh find-signal '<fingerprint>'
   ```
   Then:
   - matching OPEN issue -> SKIP (already tracked)
   - matching issue CLOSED without a fix -> SKIP (user rejected it:
     `state_reason` `not_planned`, or closed with no linked merged PR)
   - matching issue CLOSED by a merged PR, but the anomaly is back -> file a
     NEW issue, ref the old
   - no match -> file a new issue

   When unsure whether two findings are "the same", err toward SKIP and note it.

5. File a detailed issue for each anomaly that passes step 4 with:
   ```
   printf '%s' "$body" | .claude/skills/applicationinsightsscan/gh-api.sh create-issue \
     "<title>" "telemetry,reliability" -
   ```
   The body's FIRST line MUST be the `telemetry-signal:` fingerprint, then:
   concrete numbers (counts, percentiles, window), the mapped
   exception/endpoint, a correlation hypothesis where one exists, and a
   minimal next step. Typically 0–5 issues; zero is fine.

Never change code, never open a PR, never commit.

## Required environment

These scripts need outbound network access and secrets that live on the
execution environment, not in this repo:

| Requirement | Purpose |
|---|---|
| Egress to `api.applicationinsights.io` | Application Insights REST data-plane API |
| Egress to `api.github.com` | Issue search + creation via `gh-api.sh` |
| `APPINSIGHTS_APP_ID` | Application Insights app ID (GUID) for this app |
| `APPINSIGHTS_API_KEY` | API key with "Read telemetry" permission |
| `GIT_PAT` (or `GITHUB_TOKEN`) | Token with `repo` scope / Issues read-write on the target repo |
| `GH_REPO` (optional) | Overrides the auto-detected target repo (`owner/repo`) |

As a local/dev convenience, the secret rows above also fall back to a
gitignored `.env` at the repo root for any that aren't already set in the
environment (see `env-fallback.sh`) — a real environment variable always
takes precedence over the `.env` file. If any of these are missing from both
the environment and `.env`, the scripts fail fast with a clear error —
report that back to the user rather than guessing at values.
