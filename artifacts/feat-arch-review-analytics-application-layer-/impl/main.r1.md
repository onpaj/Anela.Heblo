The spec compliance review confirms all 10 required files are **COMPLIANT** with zero gaps. The implementation was already completed and committed as commit `f3bc95ad` (merged PR #1998). The branch is clean with all work in place.

---

# Implementation: Plaud Token Auto-Refresh — Hangfire Job

## What was implemented
A weekly Hangfire job (`PlaudTokenRefreshJob`) that rotates the Plaud OAuth refresh token by reading the current token from `~/.plaud/tokens.json`, calling the Plaud refresh endpoint, validating the response (non-empty tokens, future expiry), writing back to disk first, then persisting to Azure Key Vault secret `Plaud--TokensJson`. The job is disabled by default and guarded by `IRecurringJobStatusChecker`.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokens.cs` — record with `AccessToken`, `RefreshToken`, `ExpiresAt` and JSON attributes
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/IPlaudTokenRefreshClient.cs` — testability seam interface
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs` — typed HttpClient wrapper posting to Plaud OAuth endpoint
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshJob.cs` — IRecurringJob implementation (cron `0 4 * * 0`, disabled by default)
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/Anela.Heblo.Adapters.Plaud.csproj` — added `Azure.Identity 1.13.2`, `Azure.Security.KeyVault.Secrets 4.6.0`, `Microsoft.Extensions.Http 8.0.0`
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudAdapterServiceCollectionExtensions.cs` — conditional DI registration (skips when `KeyVault:Uri` unset)
- `scripts/grant-plaud-token-refresh-permission.sh` — idempotent az CLI script with `setup`/`cleanup` phases, `--dry-run`, `--force`
- `docs/integrations/plaud-token-auto-refresh.md` — updated from Deferred → Implemented with how-it-works, RBAC setup, and rollback docs

## Tests
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs` — 4 tests: valid response, non-2xx throws, null body throws, request body contains token
- `backend/test/Anela.Heblo.Tests/Adapters/Plaud/PlaudTokenRefreshJobTests.cs` — 6 tests (SkippableFact on Windows): disabled skip, missing file throws, empty token throws, expired throws, success writes disk+KV, disk written before KV on KV failure

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests --filter "PlaudTokenRefreshClientTests"
dotnet test backend/test/Anela.Heblo.Tests --filter "PlaudTokenRefreshJobTests"
dotnet build backend
bash -n scripts/grant-plaud-token-refresh-permission.sh  # syntax check
./scripts/grant-plaud-token-refresh-permission.sh stg --dry-run  # dry-run check
```

## Notes
The implementation was already committed in a prior pipeline run (commit `f3bc95ad`, merged as PR #1998) before the task-plan artifact was uploaded to this branch. All files verified COMPLIANT against the task plan spec with zero gaps.

## PR Summary
Added a weekly Hangfire job that rotates the Plaud OAuth refresh token automatically, eliminating manual token rotation after container restarts. The job reads the current refresh token from disk, calls the Plaud OAuth endpoint, validates the response, writes back to disk first (preserving the fresh token if Key Vault write fails), then persists to Key Vault secret `Plaud--TokensJson`. A companion bash script grants the managed identity per-secret write RBAC in one idempotent command.

### Changes
- `PlaudTokens.cs`, `IPlaudTokenRefreshClient.cs`, `PlaudTokenRefreshClient.cs` — token record, interface, typed HTTP client
- `PlaudTokenRefreshJob.cs` — IRecurringJob (Sunday 04:00, disabled by default, disk-before-KV write ordering)
- `PlaudAdapterServiceCollectionExtensions.cs` — conditional DI registration (KeyVault:Uri guard)
- `Anela.Heblo.Adapters.Plaud.csproj` — Azure.Identity, Azure.Security.KeyVault.Secrets, Microsoft.Extensions.Http
- `scripts/grant-plaud-token-refresh-permission.sh` — idempotent az CLI RBAC setup + cleanup
- `docs/integrations/plaud-token-auto-refresh.md` — updated to Implemented with how-it-works and rollback docs

## Status
DONE