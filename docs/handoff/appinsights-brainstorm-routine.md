# Handoff: App Insights + GitHub brainstorm routine

**Branch:** `claude/friendly-johnson-bsocsm`
**Goal:** A periodic "brainstorm" routine that reads production telemetry from
Azure Application Insights + GitHub activity and produces improvement ideas /
risk signals.

## First thing to do in the new session

Run the connectivity self-test:

```bash
./scripts/monitoring/appinsights-query.sh --test
```

- **`OK — authenticated and reachable`** → egress + secrets are live. Proceed to "Next steps".
- **`Host not in allowlist`** → egress allowlist not applied to this container yet (see Blockers).
- **`APPINSIGHTS_APP_ID is not set`** → secrets not injected into this container (see Blockers).

## Status

| Item | State |
| --- | --- |
| GitHub access (MCP, scoped to `onpaj/anela.heblo`) | ✅ Working |
| Query script `scripts/monitoring/appinsights-query.sh` | ✅ Committed & pushed |
| App Insights API key validity | ✅ Rotated, stored as env secret |
| Egress to `api.applicationinsights.io` | ⏳ Saved in env config; needs fresh container to take effect |
| Env secrets injected into session | ⏳ Same — applies on new container only |

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
  - `APPINSIGHTS_API_KEY` = `<rotated key — not stored in repo>`

The script reads both from the environment; no secrets live in the repo. Per
CLAUDE.md, secrets belong in Key Vault / encrypted env, never in repo or Portal
App Settings.

## How to query

```bash
# arbitrary KQL, default last 24h
./scripts/monitoring/appinsights-query.sh 'requests | summarize count() by resultCode'

# custom ISO-8601 timespan (e.g. last 7 days)
./scripts/monitoring/appinsights-query.sh --timespan P7D 'exceptions | summarize count() by type'
```

## Next steps (not yet done)

1. Verify connection (`--test`) once on a fresh container.
2. Define the brainstorm KQL set, e.g.:
   - error/failure-rate trend: `requests | summarize failed=countif(success==false), total=count() by bin(timestamp, 1h)`
   - new/changed exception types: `exceptions | summarize count() by type, problemId`
   - slow dependencies: `dependencies | summarize p95=percentile(duration,95) by target`
   - traffic shifts: `requests | summarize count() by bin(timestamp,1h), name`
3. Pull GitHub context via MCP (recent commits, open issues, PR activity) for the
   same window.
4. Decide output format (summary doc / issue / list of ideas) and cadence.
5. Wire it as a Claude Code **routine** (see docs.claude.com routines) on this
   environment so egress + secrets are already configured.

## Useful facts
- `az` CLI is NOT installed in the sandbox; we use the REST data-plane API directly.
- GitHub in web sessions is via MCP tools (not `gh` CLI, despite CLAUDE.md note).
