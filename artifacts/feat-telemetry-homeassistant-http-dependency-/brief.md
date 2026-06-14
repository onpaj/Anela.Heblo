telemetry-signal: dep-fail:HTTP:homeassistant.tail0cdb23.ts.net

## Signal

In the last 7 days (P7D, ending 2026-06-12T15:12Z):

- **32 dependency failures** (`type: HTTP`, `target: homeassistant.tail0cdb23.ts.net`, `resultCode: Faulted`) in the dependency telemetry.
- **32 `System.IO.IOException`** at `Anela.Heblo.Adapters.HomeAssistant.HomeAssistantConditionsReadingProvider+<FetchSensorValueAsync>d__7.MoveNext` — the matching exception records.

**Rate: ~4.6 failures/day, consistent and ongoing** (4 IOExceptions confirmed in the last 2 days).

Latency context for successful calls (3,768 total calls in P7D): p50 317 ms, p95 616 ms, p99 814 ms — within normal range. The 32 Faulted failures are distinct from latency; they are connection-level errors.

## Correlation

`homeassistant.tail0cdb23.ts.net` is the Tailscale hostname for the local Home Assistant instance. A `resultCode: Faulted` dependency + `IOException` at `FetchSensorValueAsync` indicates TCP-level connection failures (TCP reset, connection refused, or network path unreachable). There is no merged PR in the window addressing the HomeAssistant adapter, and the failure rate is stable rather than spiking — pointing to persistent intermittent Tailscale tunnel or HA availability issues rather than a code regression.

## Next step

Check Tailscale peer status for `tail0cdb23.ts.net` and Home Assistant uptime/restart logs for the same period (June 5–12). If HA is briefly unavailable on a schedule (e.g. nightly restarts), confirm that `HomeAssistantConditionsReadingProvider` has a timeout + graceful-degradation path — it should return a cached or default sensor value rather than propagating the `IOException` to callers when HA is momentarily unreachable.