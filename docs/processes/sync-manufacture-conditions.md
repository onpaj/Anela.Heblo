---
process: sync-manufacture-conditions
kind: sync
module: manufacture
summary: Reads production-room and outdoor temperature and humidity from Home Assistant, shows them on a dashboard tile and freezes a snapshot onto each manufacture order when its semi-product and its products are confirmed, for the batch protocol.
owns:
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureConditionsCaptureService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureConditionsCaptureService.cs
  - backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/ManufactureConditionsTile.cs
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/**
  - backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs
  - backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs
  - backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/**
verified_at: "d2d438ec3"
related:
  - flow-manufacture-order
  - flow-manufacture-pdfs
---

# Production-room conditions (Home Assistant)

## Purpose
Cosmetics production records the conditions a batch was made in. Heblo reads four sensors from
Anela's Home Assistant — inside the production room (výrobna) and outside — and:
- shows the live values on the dashboard tile "Podmínky ve výrobně";
- stores one reading per order and stage (semi-product made, products completed) in
  `ManufactureOrderConditionsReadings`, shown on the order detail and printed in the
  manufacture protocol (`flow-manufacture-pdfs`).

## Trigger
- Order capture: inside the status change to **SemiProductManufactured** or **Completed**
  (`UpdateManufactureOrderStatusHandler`), only if the order has no reading for that stage yet.
- Tile: whenever a dashboard with the tile is loaded.
No scheduled job; reads are cached 5 min in memory.

## Data flow
1. `HomeAssistantConditionsReadingProvider.GetCurrentSnapshotAsync`:
   - memory cache key `HomeAssistant_ConditionsSnapshot` hit → return it;
   - else one fetch at a time (single-flight gate): four parallel
     `GET {HomeAssistant:BaseUrl}/api/states/{entityId}` with a bearer token, take `state` as a
     decimal (invariant culture); `unavailable` / `unknown` / non-numeric / non-2xx → null.
2. Source: 4 values → Live; 1–3 → Partial; 0 → fall back to the last Live snapshot if it is at most
   60 min old (marked **Stale**), else **Unavailable** (all null). Live/Partial snapshots are cached.
3. Order capture copies the snapshot into a new `ManufactureOrderConditionsReadings` row
   (`Stage`, 4 values numeric(5,2), `RecordedAt` UTC, `Source`); any exception → a row with
   `Source = Unavailable` and no values. Saved with the state change; unique per (order, stage).

## Logic & formulas
- Units: °C and % relative humidity, as Home Assistant reports them; no conversion.
- Sensors (repo config): inside `sensor.temp17_vytobna_temperature` / `sensor.temp17_vytobna_humidity`,
  outside `sensor.d32_kurnik_kurnik_venku_teplota` / `sensor.d32_kurnik_kurnik_venku_vlhkost`.
- Retries: per sensor up to `RetryCount` (2) on 5xx, network errors and timeouts, exponential
  back-off from 200 ms (max 2 s), per-attempt timeout `RequestTimeoutSeconds`.
- Circuit breaker (`CircuitBreakerEnabled`): the HTTP pipeline order is retry (outer) -> circuit breaker -> per-attempt timeout (inner). The circuit opens when the failure ratio reaches 0.5 with at least 8 calls in a 60 s sampling window, and stays open 60 s before a trial call. Per-attempt timeouts count as failures. While open, calls fail immediately with no network call and are not retried; each sensor read returns null, so the snapshot degrades to Stale (recent Live snapshot) or Unavailable. Open/close transitions are logged (`Anela.Heblo.Adapters.HomeAssistant.CircuitBreaker`).
- A reading is never refreshed: reverting and re-confirming an order keeps the first reading of that stage.
- Health check `HomeAssistantConditionsHealthCheck`: Healthy only when the last snapshot is Live and
  ≤ `LiveSnapshotMaxAgeMinutes` old, otherwise Degraded (never Unhealthy).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `HomeAssistant:BaseUrl`, `HomeAssistant:AccessToken` | secrets (Key Vault `HomeAssistant--BaseUrl`, …) | HA address and long-lived token; missing/invalid URL → every read Unavailable |
| `HomeAssistant:InnerTemperatureEntityId` … `OuterHumidityEntityId` | see Logic | Sensor entity ids |
| `HomeAssistant:RequestTimeoutSeconds` | 10 (appsettings; class default 3) | Per-attempt timeout |
| `HomeAssistant:ConditionsCacheDurationMinutes` | 5 | Snapshot cache |
| `HomeAssistant:RetryCount` / `RetryBaseDelayMilliseconds` / `RetryMaxDelaySeconds` | 2 / 200 / 2 (class defaults) | Retry policy |
| `HomeAssistant:CircuitBreakerEnabled` | true (class default) | Turns the circuit breaker on/off |
| `HomeAssistant:CircuitBreakerFailureRatio` | 0.5 (class default) | Failure ratio in the sampling window that opens the circuit |
| `HomeAssistant:CircuitBreakerMinimumThroughput` | 8 (class default) | Minimum calls in the window before the circuit can open |
| `HomeAssistant:CircuitBreakerSamplingDurationSeconds` | 60 (class default) | Sampling window |
| `HomeAssistant:CircuitBreakerBreakDurationSeconds` | 60 (class default) | Open duration before a trial call |
| `HomeAssistant:StaleSnapshotMaxAgeMinutes` | 60 (class default) | How old a Live snapshot may be served as Stale; 0 disables |
| `HomeAssistant:LiveSnapshotMaxAgeMinutes` | 15 (class default) | Health-check freshness |

## Runtime facts
None.

## Known quirks
- **A Stale reading carries the original measurement time** (`RecordedAt` of the last Live
  snapshot), so an order can show conditions measured up to an hour before confirmation.
- **Unavailable is silent**: a missing token or a down Home Assistant never blocks a
  confirmation; the order just gets an empty reading.
- Worst-case wait with the repo values: (10 s × 3 attempts + 1 s) for the gate, so a confirmation can be delayed ~30 s by an unreachable Home Assistant. This is unchanged while the circuit is closed; once it is open, reads fail immediately.
- **Tailscale tunnel instability** to Home Assistant has caused repeated telemetry anomalies (#2997, #3288, #3396, #3651). Signature: HTTP dependency failures with an SSL handshake EOF (`The SSL connection could not be established`). The circuit breaker (#3651) limits the load and noise during such outages.
- **Breaker state is per instance and in memory**: it resets on restart/redeploy and is not shared between instances.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs` — fetch, cache, Live/Partial/Stale/Unavailable
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Resilience/HomeAssistantCircuitBreakerOptionsFactory.cs` — breaker options from settings (out-of-range values clamped), state-change logging
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs` — HttpClient, retry, circuit breaker, timeout
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureConditionsCaptureService.cs` — snapshot → order reading
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/ManufactureConditionsTile.cs` — dashboard tile
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs` — table, unique (order, stage)
