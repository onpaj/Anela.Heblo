### task: update-process-doc

**Files:** Modify `docs/processes/sync-manufacture-conditions.md`.

- [ ] Logic section: add circuit breaker bullet (retry -> breaker -> timeout; ratio 0.5, min throughput 8, sampling 60 s, break 60 s; open circuit => no network calls, degrades to Stale/Unavailable).
- [ ] Configuration table: add the five `HomeAssistant:CircuitBreaker*` keys with defaults.
- [ ] Known quirks / Runtime facts: Tailscale tunnel instability history (#2997, #3288, #3396, #3651), SSL handshake EOF signature, breaker state is per-instance in memory and resets on restart, worst-case wait note unchanged for closed circuit.
- [ ] Update Code entry points description (retry, breaker, timeout); bump `verified_at` to current HEAD short SHA.
- [ ] Run `python3 scripts/process-docs/check.py index`; passes.
- [ ] Commit: `docs(processes): HomeAssistant circuit breaker`.

