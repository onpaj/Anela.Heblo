telemetry-signal: exception:PlaudAuthExpiredException@PlaudCliClient.RunCliAsync

## Signal

`Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException` at `PlaudCliClient.RunCliAsync` in the exceptions table:

| Window | Count |
|---|---|
| P7D (ending 2026-06-12T15:12Z) | 17 |
| 2026-06-12 alone | 18 |

All 18 today share the same `problemId`: `Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException at Anela.Heblo.Adapters.Plaud.PlaudCliClient+<RunCliAsync>d__7.MoveNext`. The weekly digest captured 17 before more fired during the same session — the exception is actively occurring at the time of this filing.

## Pattern

Near-zero occurrences in the first 5 days of the window, then a surge on 2026-06-12 (18 in one day). This step-change is consistent with a Plaud session token that expired and now causes every trigger to fail immediately.

## Correlation

No PR in the 7-day window touches `PlaudCliClient` or the Plaud adapter credentials. There is no evidence of an automatic token-refresh mechanism in the exception stack — the failure propagates from `RunCliAsync` without a retry path.

## Next step

Re-authenticate the Plaud CLI session and rotate credentials in Azure Key Vault (`kv-heblo-prod`). After re-auth, verify exceptions stop. Longer-term, add a proactive token-expiry check or automatic refresh in `PlaudCliClient` to avoid requiring manual intervention on each expiry cycle.