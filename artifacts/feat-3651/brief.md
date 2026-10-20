telemetry-signal: dep-fail:HTTP:homeassistant.tail0cdb23.ts.net

## Signal

The HomeAssistant HTTP dependency (`homeassistant.tail0cdb23.ts.net`) failed again today, 2026-07-14 — the fourth occurrence of this signal after three prior closures (#2997, #3288, #3396), none of which addressed the underlying connectivity root cause.

**Window:** all events fall on 2026-07-14 (UTC); the preceding 6 days (07-08 to 07-13) show zero HA dependency exceptions in this window — this is a fresh, discrete recurrence, not a continuation.

| Metric | Value (today, 2026-07-14) |
|---|---|
| Dependency calls | 912 |
| Faulted | 48 (5.3%) |
| `System.IO.IOException` exceptions at `HomeAssistantRetryActivityTaggingHandler+<SendAsync>d__1.MoveNext` | 240 (retries amplify exception count vs. faulted-call count — same amplification pattern noted in #3396) |
| P7D dependency latency (whole-window figure) | p50 376ms / p95 757ms / p99 2,262ms |

Hourly exception clusters: **11:00 UTC (60)**, **16:00 UTC (160)**, **20:00 UTC (20)** — three discrete bursts through the day, not a continuous outage.

**Exception chain** (sampled 2026-07-14T20:08:58Z):
```
HttpRequestException: The SSL connection could not be established, see inner exception.
 ---> IOException: Received an unexpected EOF or 0 bytes from the transport stream.
   at System.Net.Security.SslStream+<ReceiveHandshakeFrameAsync>...
   at Anela.Heblo.Adapters.HomeAssistant.Resilience.HomeAssistantRetryActivityTaggingHandler+<SendAsync>d__1.MoveNext()
     in /src/backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantRetryActivityTaggingHandler.cs:line 20
```
Same "dead Tailscale tunnel" signature diagnosed in #3288/#3396 — the SSL handshake aborts mid-negotiation (EOF), not a timeout.

**User impact this time is contained:** `GET Dashboard/GetTileData` (the endpoint that calls the HA adapter) only failed 2 of 2,743 calls today — the retry/fallback path is absorbing most of the instability without surfacing errors to users. No `/health` cascade or browser-side 503 spike observed today (unlike #3396's 2026-06-26/06-29 mass-failure event, which reached 98.9% failure and cascaded into Azure health-probe 503s).

## History

- **#2997** (closed `completed` 2026-06-14) — fixed via PR #3045 (retry handler added).
- **#3288** (closed `completed` 2026-06-22) — grooming-only closure; root cause acknowledged as outside-repo Tailscale tunnel instability. Circuit breaker recommended but not scoped into its own issue.
- **#3396** (closed `completed` 2026-07-08) — escalated to a mass-failure event (up to 98.9% failure rate, 2026-06-26 to 06-29). PR #3420 fixed the `/health` cascade side-effect (made the HA check non-fatal), but explicitly did **not** restore HA connectivity — the issue's own closing comments state the Tailscale tunnel was "still unreachable" at close time. Circuit breaker recommended again, still not filed/implemented.
- **This issue** — new recurrence, contained scale (5.3% failure rate vs. #3396's peak 98.9%), no cascade this time (thanks to the #3420 health-check fix holding).

## Correlation hypothesis

Consistent with all three prior filings: intermittent Tailscale tunnel instability to the self-hosted HomeAssistant instance, not an application bug. Checked commits merged 2026-07-14 (12 total) — none touch the HomeAssistant adapter, so this isn't a same-day regression from a deploy.

## Next step

The Polly circuit breaker recommended in both #3288's and #3396's grooming ("fail-fast after N consecutive SocketException/IOException, with cooldown window") still has no matching merged PR. Given this is now the 4th occurrence of the identical signal, recommend:
1. Actually scope and file the circuit-breaker sub-issue this time (it was proposed twice and dropped twice) — bound the retry-amplification (240 exceptions from 48 faulted calls) and fail fast instead.
2. One-time check of Tailscale admin / HA host logs around 2026-07-14 11:00, 16:00, and 20:00 UTC — three discrete burst timestamps in one day may point to a periodic cause (scheduled restart, key rotation, DHCP renewal) rather than pure network flakiness.
