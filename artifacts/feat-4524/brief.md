## Module / File
`backend/src/Anela.Heblo.API/Infrastructure/Telemetry/CostOptimizedTelemetryProcessor.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)

## What's not tested
The processor intercepts all Application Insights telemetry and applies multiple filtering rules — none tested:

1. **Request filtering** — drops health-check paths, static-file extensions, successful OPTIONS (CORS preflight), and fast (< 10 ms) successful requests. If the path-prefix check has a bug, health probes appear in AI dashboards; if it drops too broadly, a 500 from `/api/health` would be silently discarded.
2. **Dependency filtering** — always passes through failed dependencies, drops successful SQL calls < 50 ms and successful HTTP calls < 100 ms. The `success == false` early-pass must fire before the duration checks; the order matters because a fast failed dependency must not be filtered.
3. **Trace suppression** — reads `ASPNETCORE_ENVIRONMENT`; in Production, Verbose and Information traces are dropped. Any other environment (Staging, Development) must pass all traces. If the environment variable lookup is wrong, debug traces flood the Production AI workspace.

## Why it matters
A bug in rule 2 (e.g., the `success == false` short-circuit is lost in a refactor) would silently drop failed dependency telemetry from Application Insights, making it impossible to detect database or HTTP failures in production without separate alerting. A bug in rule 3 would either flood the AI workspace with verbose logs (cost spike) or suppress warnings in Production.

## Suggested approach
Unit-test `CostOptimizedTelemetryProcessor.Process` with a stub `ITelemetryProcessor` next:
- Failed dependency (any duration) — must call `_next.Process`.
- Fast successful SQL call (30 ms) — must NOT call `_next.Process`.
- Slow successful SQL call (60 ms) — must call `_next.Process`.
- Successful OPTIONS request — must NOT call `_next.Process`.
- Fast successful request (5 ms) — must NOT call `_next.Process`.
- Failed fast request (5 ms) — must call `_next.Process`.
- Information trace in Production — must NOT call `_next.Process`.
- Warning trace in Production — must call `_next.Process`.
- Information trace in Staging — must call `_next.Process`.

Effort: ~1 h.

---
_Filed by weekly coverage-gap routine on 2026-10-19. Based on CI run #38726815029 (263a263200d3e8a3df47581d57e8bd8c148a7336)._
