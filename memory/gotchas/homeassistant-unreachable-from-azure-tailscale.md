# HomeAssistant is unreachable from Azure (Tailscale-only) → was failing /health with 503

## Symptom

Both `https://heblo.anela.cz` (prod) and `https://heblo.stg.anela.cz` (staging) returned
**`/health` → 503** and **`/health/ready` → 503** indefinitely, even after running for hours,
while the app otherwise worked. `/health/live` stayed 200.

The app kept running despite the 503 only because Azure's `healthCheckPath` is **`null`** on both
web apps (`az webapp show --query siteConfig.healthCheckPath`), so Azure never recycled the
container on health. But the Docker `HEALTHCHECK` (`curl -f http://localhost:8080/health`,
`Dockerfile`) hits the aggregate `/health` and marked the container unhealthy in Azure status.

## Root cause

The aggregate `/health` endpoint has no predicate → it includes every registered check → any one
`Unhealthy` makes the whole endpoint 503. The only failing check was `homeassistant-conditions`,
which returned `Unhealthy` with `source: "Unavailable"`.

HA is unavailable in cloud because KV secret `HomeAssistant--BaseUrl` (both `kv-heblo-stg` and
`kv-heblo-prod`) = `https://homeassistant.tail0cdb23.ts.net` — a **Tailscale MagicDNS** hostname
resolving to `100.98.187.44` (Tailscale CGNAT `100.64.0.0/10`). It is reachable only from a
tailnet-connected machine (a tailnet host gets HTTP 401 = HA alive, needs token). The **Azure Web
App is not a Tailscale node**, so it can neither resolve `*.ts.net` (MagicDNS is tailnet-only) nor
route to `100.x` addresses. Every per-sensor fetch fails → all 4 sensors null → `Source =
Unavailable`. This is a deployment/networking gap, not a code defect.

## Fix applied (health semantics — makes HA non-fatal)

The check was *registered* with `failureStatus: HealthStatus.Degraded` (clear intent: never
hard-fail app health), but its body hardcoded `HealthCheckResult.Unhealthy(...)` for the
`Unavailable` / no-snapshot / unknown-source cases — and `failureStatus` only applies to *thrown*
exceptions, not explicitly-returned statuses. Two surgical changes:

1. `HomeAssistantConditionsHealthCheck.cs` — failure branches now return `Degraded` (never
   `Unhealthy`). `Degraded` maps to HTTP 200 in the default `MapHealthChecks` mapping, so `/health`
   goes green even when HA is down.
2. `ServiceCollectionExtensions.cs` — dropped the `"ready"` tag from `homeassistant-conditions`
   (tags now `["homeassistant"]`), so it no longer appears in `/health/ready`. It still appears in
   the unfiltered `/health`, now as `Degraded`.

## Still open — actual HA connectivity (separate decision, NOT implemented)

The code fix only makes HA unreachability harmless. To actually read HA conditions from cloud,
pick one:

- **A — accept cloud-unreachable.** HA tiles show stale/unavailable in cloud; nothing else to do.
- **B — Tailscale Funnel.** Expose HA via Funnel for a public `*.ts.net` HTTPS endpoint and point
  `HomeAssistant--BaseUrl` at it. Exposes HA to the internet.
- **C — Tailscale in the container.** Run a userspace Tailscale client (auth key in KV) so the
  Azure app joins the tailnet. Most infra work; keeps HA private.

## Lesson

A non-critical external integration (HA conditions monitoring) must never be able to return
`Unhealthy` from a check that lands in the unfiltered `/health` aggregate — it will 503 the whole
app's health and flip the container's Docker health status. Use `Degraded` for optional
dependencies, and keep them out of the readiness predicate.
