telemetry-signal: exception:SocketException@Polly.Outcome.GetResultOrRethrow

**Window:** P7D (2026-06-10 – 2026-06-17)
**Post-fix occurrences:** 19 (Jun 15–16), all after the PR #3028 / #3045 resilience merges
**Total in window:** 31 (25 × `at Polly.Outcome.GetResultOrRethrow` + 6 plain)

## Signal

`System.Net.Sockets.SocketException` surfacing through `Polly.Outcome\`1.GetResultOrRethrow` — a Polly-wrapped outbound call exhausted every retry attempt after TCP socket timeouts.

### Post-fix timeline (after 2026-06-14T18:00 UTC)

| Timestamp (UTC) | Type | Timeout | Count |
|---|---|---|---|
| Jun 15 00:05:48 | `at Polly.Outcome…` | 30 s | 2 |
| Jun 15 01:06:18 | `at Polly.Outcome…` | 30 s | 2 |
| Jun 15 02:06:48 | `at Polly.Outcome…` | 30 s | 2 |
| Jun 16 06:39:21 | plain (8 s timeout) | 8 s | 2 |
| Jun 16 06:39:31 | plain (8 s timeout) | 8 s | 2 |
| Jun 16 06:39:40 | plain (8 s timeout) | 8 s | 2 |
| Jun 16 06:39:43 | `at Polly.Outcome…` | 30 s | 3 |
| Jun 16 11:16:55 | `at Polly.Outcome…` | 30 s | 2 |
| Jun 16 12:17:25 | `at Polly.Outcome…` | 30 s | 2 |

`outerMessage` for all 30 s entries: `"The operation didn't complete within the allowed timeout of '00:00:30'."` / for 8 s entries: `'00:00:08'`.

### Pre-fix cluster (Jun 11–14, 12 occurrences)

Same exception type also appeared Jun 11–14 during the broader connectivity storm (covered by PR #3028 Npgsql resilience, PR #3045 HomeAssistant resilience). Those pre-fix occurrences likely represent a different failure path (DB / HomeAssistant). The Jun 15–16 cluster is distinct and persists after both fixes.

## Characteristics

- **`operation_Name` is empty for all occurrences** — exceptions fire outside HTTP request scope, consistent with a Hangfire background job (no parent request operation ID).
- **Paired timestamps** — two exceptions per event at identical millisecond precision, suggesting two correlated async paths (e.g. read + write or double-logged exception in a retry handler).
- **Hourly cadence on Jun 15** at :05 past midnight, :06 past 1 AM, :06 past 2 AM → a recurring Hangfire job on an ~1 h schedule.
- **Jun 16 burst at 06:39** — 6 × 8-second inner timeouts spread over 22 seconds (3 retry attempts at ~10 s intervals), immediately followed by 3 × 30-second Polly exhaustion events. This is the retry ladder bottoming out.
- **No dependency row** — the operation ID does not join to any `dependencies` table entry, so the target host is not captured. The HTTP client making the call does not emit dependency telemetry.

## Correlation hypothesis

PR #3028 (Npgsql connection resilience) and PR #3045 (HomeAssistant resilience) addressed the pre-Jun-14 socket failures. The surviving cluster points to a **third HTTP client** that:
- Uses a per-attempt timeout of **8 s** (shorter than FlexiBee's config, plausible for a home-network or low-latency target)
- Is wrapped in a Polly policy with a **30 s** total timeout
- Is invoked by a **Hangfire recurring job** on an ~1 h schedule

Most likely candidates:
1. **PlaudTokenRefreshClient** (`platform.plaud.ai`) — 3 × `HttpRequestException at PlaudTokenRefreshClient.RefreshAsync` also in this window; the Plaud auth expiry (#3118) may have degraded connectivity to `platform.plaud.ai` beyond just the CLI.
2. **HomeAssistant** — 4 × `Faulted` dependency on `homeassistant.tail0cdb23.ts.net` remain post-PR-#3045; the 8 s timeout is consistent with a LAN/Tailscale target.
3. Unknown scheduled integration with a missing `AddDependencyTracking()` / `HttpClientFactory` registration.

## Minimal next step

1. Check Hangfire job history at **2026-06-15 00:05, 01:06, 02:06 UTC** and **2026-06-16 06:39, 11:16, 12:17 UTC** — identify which job class was executing at those exact timestamps.
2. Ensure that HTTP client registrations for that job use `IHttpClientFactory` so App Insights auto-instruments the outbound call and populates `dependencies`.
3. If the target is `platform.plaud.ai`, correlate with #3118 (PlaudAuthExpiredException) and `HttpRequestException at PlaudTokenRefreshClient.RefreshAsync` — consider whether the auth expiry is also blocking token refresh network calls.
4. If the target is HomeAssistant, verify the resilience added by PR #3045 covers the recurring-job code path (not just the request-scoped provider).
