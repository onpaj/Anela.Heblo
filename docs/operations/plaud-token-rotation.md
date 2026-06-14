# Plaud Token Rotation Runbook

> Triggered by alert: `Heblo-Plaud-AuthExpired`, `PlaudTokenNearExpiry`, or `PlaudTokenRefreshFailed`.

## When to run

Run this runbook when **any** of the following fires:
- `Heblo-Plaud-AuthExpired` (Sev 2) — `PlaudAuthExpiredException` count > 0 in 15 min.
- `PlaudTokenRefreshFailed` (Sev 2) — in-line refresh failed, runbook is now required.
- `PlaudTokenNearExpiry` (Sev 3) — proactive warning, schedule rotation before hard expiry.

The system attempts in-line + weekly refresh automatically. This runbook only fires
when the refresh token itself is dead (rotation lapsed, refresh-token revoked, etc.).

## Steps

1. **Run `plaud login` locally** to obtain a fresh tuple:
   ```bash
   plaud login
   cat ~/.plaud/tokens.json
   ```
   You'll get back JSON of the shape:
   ```json
   {"access_token":"...","refresh_token":"...","expires_at":1234567890}
   ```

2. **Rotate the Key Vault secret** — the whole JSON blob is one secret:
   ```bash
   az keyvault secret set \
     --vault-name kv-heblo-prod \
     --name "Plaud--TokensJson" \
     --value "$(cat ~/.plaud/tokens.json)"
   ```
   > Do **not** put this secret in App Service environment variables. Per `CLAUDE.md`,
   > all secrets live in Key Vault. The KV separator is `--`.

3. **Restart the Heblo Azure Web App** so the new secret is loaded into config:
   - Azure Portal → `Heblo` Web App → Restart, **or**
   - `az webapp restart --name Heblo --resource-group <rg>`

4. **Verify the `plaud-token-refresh` Hangfire job is enabled** in production:
   - Web app → `/admin/background-jobs` UI.
   - Confirm `plaud-token-refresh` shows **Enabled**. If not, enable it.
   - This is defence-in-depth — `PlaudCliClient` also self-refreshes, but the weekly
     job catches process-lifetime drift.
   - Note: `DefaultIsEnabled` in code is now `true`, but the admin-UI toggle may override it.

5. **Confirm the surge has stopped**: open Application Insights and run:
   ```kusto
   exceptions
   | where timestamp > ago(15m)
   | where problemId == "Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException at Anela.Heblo.Adapters.Plaud.PlaudCliClient+<RunCliAsync>d__7.MoveNext"
   | count
   ```
   Expected: 0 within 15 minutes of restart.

## Why this happens

- Plaud refresh tokens expire ~30 days after issue.
- `PlaudCliClient` refreshes proactively inside `ExpiryBuffer` (default 72h) and reactively on `AUTH_FAILED`.
- The weekly `plaud-token-refresh` Hangfire job is a safety net.
- This runbook only fires when **all three** paths have failed — usually because the refresh token itself is dead.

## Alert configuration (reference)

All three alerts route to action group `ag-heblo-ops` (email `ondra@anela.cz`).

| Alert | Source | Severity | Window | Eval |
|---|---|---|---|---|
| `Heblo-Plaud-AuthExpired` | `exceptions` where problemId matches PlaudAuthExpiredException, count > 0 | 2 | 15 min | 5 min |
| `PlaudTokenNearExpiry` | `customEvents` where name == "PlaudTokenNearExpiry", count > 0 | 3 | 60 min | 15 min |
| `PlaudTokenRefreshFailed` | `customEvents` where name == "PlaudTokenRefreshFailed", count > 0 | 2 | 15 min | 5 min |
