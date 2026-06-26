# Handoff: App Insights + GitHub brainstorm routine

**Goal:** A periodic "brainstorm" routine that reads production telemetry from
Azure Application Insights + GitHub activity and produces improvement ideas /
risk signals.

**Status: design complete.** The query engine, digest engine, and routine
definition are committed. The only remaining manual step is **creating the
scheduled routine in the Claude Code web UI** (see
[`docs/routines/telemetry-anomaly/README.md`](../../docs/routines/telemetry-anomaly/README.md) →
"Creating the routine").

## Connectivity self-test

Run this first in any new session/container:

```bash
./docs/routines/telemetry-anomaly/appinsights-query.sh --test
```

- **`OK — authenticated and reachable`** → egress + secrets are live.
- **`Host not in allowlist`** → egress allowlist not applied to this container.
- **`APPINSIGHTS_APP_ID is not set`** → secrets not injected into this container.

> Verified `OK — authenticated and reachable` on 2026-06-12. Live telemetry over
> the last 7 days: ~30k requests, ~91k dependencies, 254 exceptions.

## What exists now

| Item | State |
| --- | --- |
| GitHub access (REST via `gh-api.sh` + `GIT_PAT`, no MCP) | ✅ Working — `repo` scope verified |
| Query script `docs/routines/telemetry-anomaly/appinsights-query.sh` | ✅ Committed |
| Digest engine `docs/routines/telemetry-anomaly/telemetry-digest.sh` | ✅ Committed — runs the curated KQL set → Markdown |
| GitHub helper `docs/routines/telemetry-anomaly/gh-api.sh` | ✅ Committed — issue search/create via REST |
| Routine definition `docs/routines/telemetry-anomaly/README.md` | ✅ Committed — prompt, schedule, flag/skip rules |
| App Insights API key validity | ✅ Rotated, stored as env secret |
| Egress to `api.applicationinsights.io` | ✅ Verified reachable from this environment |
| Scheduled routine created in web UI | ⏳ **Last step** — create per the routine doc |

### Key learning
Environment config changes (network egress allowlist + env secrets) on Claude
Code for web apply **at container creation, not to a running session**. After
editing the environment you must start a NEW session for them to take effect.
`api.applicationinsights.io` is **not** in the default "Trusted" allowlist, so
it must be added under **Custom**.

## Config reference (set in the web UI, "edit environment")

- **Network access:** Custom, with `api.applicationinsights.io` added, and
  "Also include default list of common package managers" checked.
- **Env secrets:**
  - `APPINSIGHTS_APP_ID` = `53f2124c-ca25-42bf-907c-17b02df8d343`
  - `APPINSIGHTS_API_KEY` = `<rotated read-telemetry key — not stored in repo>`

The scripts read both from the environment; no secrets live in the repo. Per
CLAUDE.md, secrets belong in Key Vault / encrypted env, never in repo or Portal
App Settings.

## How to query / run the digest

```bash
# Arbitrary KQL, default last 24h
./docs/routines/telemetry-anomaly/appinsights-query.sh 'requests | summarize count() by resultCode'

# Custom ISO-8601 timespan (e.g. last 7 days)
./docs/routines/telemetry-anomaly/appinsights-query.sh --timespan P7D 'exceptions | summarize count() by type'

# Full brainstorm digest (default window P7D)
./docs/routines/telemetry-anomaly/telemetry-digest.sh
./docs/routines/telemetry-anomaly/telemetry-digest.sh --timespan P1D
```

## Signals seen on the first run (2026-06-12, P7D)

Baseline for whoever creates the routine — confirms the digest produces real,
actionable signal:

- **`heblo.anela.cz` browser `Fetch` — 659 calls, 659 "failed", p99 ≈ 8,794 s.**
  Almost entirely `resultCode 0` = **aborted** client requests (tab close / SPA
  nav) on startup/polling endpoints. Mostly benign — the routine filters this.
- **`/health/ready` + `/health/live` returning 500 at ~35 s p95** (a handful of
  times). Real readiness/cold-start signal — worth watching for restart loops.
- **`Articles/FeedbackList` 500 ×4** maps to
  `ArticleRepository.GetFeedbackStatsAsync` `InvalidOperationException` ×3 — a
  concrete bug, and the feedback area had active changes (#2915, #2926).
- **`GET /api/StockUpOperations/summary` 403 ×209** — high-volume forbidden to
  authenticated users; possible permission misconfig (no related merge found).
- **Dashboard 401/403 hotspots** partly explained by the just-merged dashboard
  permission work (#2962, #2912) — example of GitHub context reclassifying a
  signal as already-addressed.
- **FlexiBee `petra-tesarikova.flexibee.eu`** — 21.5k calls, p95 ≈ 5.8 s, p99 ≈
  21 s: the slowest high-volume dependency, a standing latency/timeout risk.
- **Frontend:** one `Uncaught TypeError: r.filter is not a function` — a real
  client bug worth reproducing.

## Useful facts
- `az` CLI is NOT installed in the sandbox; we use the REST data-plane API directly.
- `gh` CLI is also NOT installed. GitHub MCP tools exist but are flaky in
  scheduled runs (they disconnected mid-session), so the routine talks to the
  GitHub REST API directly via `docs/routines/telemetry-anomaly/gh-api.sh` using `GIT_PAT`
  (classic PAT, `repo` scope). This keeps the routine self-contained and matches
  the CLAUDE.md intent of not depending on MCP.
- `jq` is available; `column` is **not** — the digest formats tables with jq.
