# Manufacture Order Conditions Readings — Design

**Date:** 2026-05-06
**Status:** Draft
**Author:** Brainstorm session (Ondra + Claude)

## Context

When a manufacture order transitions through the **SemiProductManufactured** and **Completed** stages, we need to record the ambient conditions in the workshop at that moment: inner and outer temperature plus inner and outer humidity (four values per stage, eight per order). These values are read from a HomeAssistant instance running on the local network via its REST API.

The motivation is traceability — relating product quality (curing, drying, shelf-life) to the conditions in which the semiproduct and finished product were made. Today there is no record at all; values are not captured anywhere.

The desired outcome:

- Each stage transition silently captures and persists a snapshot of the four sensor values.
- The snapshot is visible on the manufacture order detail page (read-only in v1).
- HomeAssistant being unreachable must never block a state transition.
- The domain stays agnostic of HomeAssistant — the integration lives in an adapter.

## Scope

**In scope (v1):**

- Domain port `IConditionsReadingProvider` and value record `ConditionsSnapshot`.
- New adapter project `Anela.Heblo.Adapters.HomeAssistant` with `HomeAssistantConditionsReadingProvider`.
- New entity `ManufactureOrderConditionsReading` with EF configuration and migration.
- Hooks into the two existing workflows (`ConfirmSemiProductManufactureWorkflow`, `ConfirmProductCompletionWorkflow`).
- Read-only display on the manufacture order detail page.

**Out of scope (v1):**

- Manual edit of recorded readings.
- Per-template / per-product sensor selection.
- Retries, polling, or scheduled snapshots.
- E2E tests (the nightly suite covers golden paths; backend integration tests cover this contract).

## Architecture

### Domain port (no HomeAssistant references)

Location: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/`

```csharp
public interface IConditionsReadingProvider
{
    Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken ct);
}

public sealed record ConditionsSnapshot(
    decimal? InnerTemperature,
    decimal? InnerHumidity,
    decimal? OuterTemperature,
    decimal? OuterHumidity,
    DateTime RecordedAt,
    ConditionsReadingSource Source);

public enum ConditionsReadingSource
{
    Live = 1,        // all four values present
    Partial = 2,     // some values present, some null
    Unavailable = 3, // no values
}
```

### HomeAssistant adapter

New project: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/`

Mirrors the existing pattern of `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/` and `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/`:

- `HomeAssistantSettings` — `BaseUrl`, `AccessToken`, `InnerTemperatureEntityId`, `InnerHumidityEntityId`, `OuterTemperatureEntityId`, `OuterHumidityEntityId`, `RequestTimeoutSeconds` (default 3).
- `HomeAssistantConditionsReadingProvider : IConditionsReadingProvider` — typed `HttpClient`, calls `GET /api/states/{entity_id}` four times in parallel via `Task.WhenAll`, parses the `state` JSON field as `decimal` (returns null if value is `"unavailable"`, `"unknown"`, missing, or non-numeric), composes a `ConditionsSnapshot`, and sets `Source` based on how many of the four calls produced a numeric value (4 → `Live`, 1–3 → `Partial`, 0 → `Unavailable`).
- `HomeAssistantAdapterServiceCollectionExtensions.AddHomeAssistantAdapter(configuration)` — binds settings, registers the typed `HttpClient` with `BaseAddress`, `Timeout`, and `Authorization: Bearer {AccessToken}` header, maps `IConditionsReadingProvider` → `HomeAssistantConditionsReadingProvider`.

The adapter never throws to its caller. Per-call exceptions and timeouts are caught, logged at `Warning` with the entity ID and exception type, and the corresponding sensor value becomes `null`.

### Workflow integration

Both workflows take a new dependency `IConditionsReadingProvider`. Files:

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`

Sequence inside each workflow (after the existing state-change call, before `SaveChangesAsync`):

1. Call `provider.GetCurrentSnapshotAsync(ct)`. The provider never throws; if exceptional behaviour does occur, the workflow catches it, logs, and synthesises an `Unavailable` snapshot so the transition can still complete.
2. Construct a new `ManufactureOrderConditionsReading` with `Stage` = the stage just transitioned to, copying all four values + `RecordedAt` + `Source` from the snapshot.
3. Add it to `order.ConditionsReadings`.
4. Existing `SaveChangesAsync` persists the state change and the reading row in the same transaction.

### Data model

New entity `ManufactureOrderConditionsReading` in `backend/src/Anela.Heblo.Domain/Features/Manufacture/`:

| Column | Type | Notes |
|---|---|---|
| Id | int (identity) | PK |
| ManufactureOrderId | int | FK → ManufactureOrder, indexed, cascade delete |
| Stage | enum (`SemiProductManufactured` = 4, `Completed` = 5) | reuses `ManufactureOrderState` numeric values |
| InnerTemperature | decimal(5,2)? | °C |
| InnerHumidity | decimal(5,2)? | % |
| OuterTemperature | decimal(5,2)? | °C |
| OuterHumidity | decimal(5,2)? | % |
| RecordedAt | datetime2 | UTC |
| Source | enum (`Live`, `Partial`, `Unavailable`) | stored as int |

Unique index on `(ManufactureOrderId, Stage)`.

EF configuration: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs`. Navigation collection added to `ManufactureOrder.ConditionsReadings` with EF config in `ManufactureOrderConfiguration.cs`.

New EF Core migration in `backend/src/Anela.Heblo.Persistence/Migrations/`. Per project convention, migrations are applied manually — no automatic apply on startup.

### API & DTO

`GetManufactureOrderDetailHandler` projects the new collection into the existing `ManufactureOrderDetailDto`:

```csharp
public class ManufactureOrderConditionsReadingDto
{
    public int Id { get; set; }
    public ManufactureOrderState Stage { get; set; }
    public decimal? InnerTemperature { get; set; }
    public decimal? InnerHumidity { get; set; }
    public decimal? OuterTemperature { get; set; }
    public decimal? OuterHumidity { get; set; }
    public DateTime RecordedAt { get; set; }
    public ConditionsReadingSource Source { get; set; }
}
```

DTO is a class, not a record (project rule: OpenAPI generators mishandle record parameter order). The TypeScript client regenerates on build.

### Frontend

New component: `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx`.

Rendered on `ManufactureOrderDetail.tsx` in the existing detail layout (placed under the basic-info section, alongside notes). Two-row table, columns: Stage, Inner T (°C), Inner RH (%), Outer T (°C), Outer RH (%), Recorded at.

States:

- **No readings yet** — empty rows with em-dashes.
- **Some sensor null** — em-dash in that cell only.
- **Source = Unavailable** — small red badge "HA nedostupný" next to the recorded-at timestamp.
- **Source = Partial** — small amber badge "Částečné".
- **Source = Live** — no badge.

No edit affordance in v1.

### Configuration & secrets

Settings under `HomeAssistant:` in `appsettings.json` (placeholders only):

```json
"HomeAssistant": {
  "BaseUrl": "-- stored in secrets.json --",
  "AccessToken": "-- stored in secrets.json --",
  "InnerTemperatureEntityId": "sensor.workshop_inner_temperature",
  "InnerHumidityEntityId": "sensor.workshop_inner_humidity",
  "OuterTemperatureEntityId": "sensor.workshop_outer_temperature",
  "OuterHumidityEntityId": "sensor.workshop_outer_humidity",
  "RequestTimeoutSeconds": 3
}
```

Real values:

- **Local dev:** edit `secrets.json` directly (per user preference — never `dotnet user-secrets set`).
- **Production:** Azure App Service application settings (existing pattern, no Key Vault wiring needed).

DI wiring added to `Program.cs` next to other adapter registrations (`AddShoptetApi`, etc.):

```csharp
builder.Services.AddHomeAssistantAdapter(builder.Configuration);
```

## Error handling

| Failure | Outcome |
|---|---|
| HA host unreachable / DNS fail | All four calls fail → `Source = Unavailable`, all four values null, transition succeeds, warning log. |
| HA returns 401 (bad token) | Same as unreachable. Single `Error`-level log with hint to check token. |
| HA returns 404 for one entity | That sensor's value null, other three may succeed → `Source = Partial`. Warning log naming the entity ID. |
| HA returns `state: "unavailable"` / `"unknown"` | That sensor's value null. `Source = Partial` if others succeed. Warning log. |
| HA returns non-numeric `state` | That sensor's value null. Warning log. |
| HTTP timeout (3s default) | All sensors that timed out → null. Warning log per entity. |
| Workflow exception during reading construction | Caught, logged at `Error`, synthesised `Unavailable` reading saved. State transition still succeeds. |

## Testing

### Backend unit tests — adapter

Location: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/` (new project, mirrors `Anela.Heblo.Adapters.Shoptet.Tests` structure).

Pattern: `Mock<HttpMessageHandler>` wrapped in `new HttpClient(handler.Object)`, identical to `ShoptetPriceClientTests.cs`.

Cases:

- All four sensors return numeric values → `Source = Live`, all four numbers populated.
- One sensor 404, three OK → `Source = Partial`, one null.
- All four 500 → `Source = Unavailable`, all null.
- One sensor returns `"state": "unavailable"` → null, others fine.
- One sensor returns `"state": "12.3 °C"` (suffixed) → parsed as `12.3` if reasonable, else null. (Decision: strict numeric parse only, no unit stripping; HA returns plain decimal in `state` for sensors with `unit_of_measurement`.)
- HTTP timeout on one call → that sensor null, others fine.
- 401 response → all null, error log.
- Cancellation token cancels → propagates `OperationCanceledException` (this is the one case the provider does not swallow, since it represents the workflow being cancelled).

### Backend unit tests — workflows

Location: existing `backend/test/Anela.Heblo.Tests/Features/Manufacture/`.

Add cases to the existing handler tests (or new test classes alongside):

- `ConfirmSemiProductManufactureWorkflow` — provider returns `Live` snapshot → reading row exists with `Stage = SemiProductManufactured`, all four values, `Source = Live`.
- Same workflow — provider returns `Unavailable` snapshot → reading row exists with all nulls and `Source = Unavailable`.
- `ConfirmProductCompletionWorkflow` — same two cases for `Stage = Completed`.
- Both workflows — provider throws → caught, `Unavailable` row saved, state still transitioned.
- Existing state-transition assertions remain green.

Mock `IConditionsReadingProvider` directly — no `HttpMessageHandler` here.

### Frontend tests

Location: alongside the component, `frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx`.

- Renders empty state when no readings.
- Renders both rows when both stages have readings.
- Renders red badge for `Unavailable`, amber for `Partial`, none for `Live`.
- Renders em-dash for null cells.

## Critical files

**New:**

- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/IConditionsReadingProvider.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsSnapshot.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs`
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs`
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddManufactureOrderConditionsReadings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`
- `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/...`
- `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx`
- `frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx`

**Modified:**

- `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs` — add `ConditionsReadings` collection.
- `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConfiguration.cs` — configure navigation.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` — inject provider, capture snapshot.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` — inject provider, capture snapshot.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrderDetail/...` — extend handler + DTO.
- `backend/src/Anela.Heblo.API/Program.cs` — register adapter via `AddHomeAssistantAdapter`.
- `backend/src/Anela.Heblo.API/appsettings.json` — `HomeAssistant:` section with placeholders.
- `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx` — render new section.

**Reused (no change):**

- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — pattern reference for typed-HttpClient adapter.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetPriceClientTests.cs` — pattern reference for HTTP mock tests.

## Verification

1. **Build & format**
   - `dotnet build` from repo root → green.
   - `dotnet format` → no diff.
   - `cd frontend && npm run build && npm run lint` → green.

2. **Unit tests**
   - `dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests` — all cases pass.
   - `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Manufacture"` — workflow tests pass, including new conditions-snapshot assertions.
   - `cd frontend && npm test -- ConditionsReadingsSection` — green.

3. **Migration**
   - `dotnet ef migrations add AddManufactureOrderConditionsReadings --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` produces the expected up/down.
   - Apply manually to dev DB and confirm table + index.

4. **Manual end-to-end (dev)**
   - Configure `HomeAssistant:*` settings in `secrets.json` against the real workshop HA instance with a Long-Lived Access Token.
   - Start the app. Open an existing draft manufacture order, advance to `SemiProductManufactured`. On the detail page, the new section shows one row with current sensor values and `Recorded at` timestamp.
   - Advance to `Completed`. Section now shows two rows.
   - Stop HA (or break the token). Create another order, advance through both stages. Both rows show em-dashes plus the red "HA nedostupný" badge. State transitions still succeed.

5. **Logs**
   - `Warning`-level log appears when any single sensor fails.
   - `Error`-level log appears for total failure or auth issue.
   - No PII leaks in either path.

## Open questions

None as of this draft.
