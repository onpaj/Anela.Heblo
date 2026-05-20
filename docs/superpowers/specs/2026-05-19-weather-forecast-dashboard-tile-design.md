# Weather Forecast Dashboard Tile — Design Spec

**Date:** 2026-05-19
**Status:** Approved

---

## Overview

Add a dashboard tile that surfaces the 5-day weather forecast (hottest place in CZ per day) already displayed in the Cooling tab. The tile reuses existing backend logic (`IWeatherForecastClient`) and existing frontend utilities (`temperatureScale.ts`, `weatherIcons.ts`). No new API surface is needed.

---

## Tile Metadata

| Property | Value |
|---|---|
| Tile ID | `weatherforecast` (derived from class name `WeatherForecastTile`) |
| Title | `Předpověď počasí` |
| Description | `5denní předpověď počasí — nejteplejší místo v ČR` |
| Size | `Large` |
| Category | `Manufacture` |
| DefaultEnabled | `false` (opt-in) |
| AutoShow | `true` |
| RequiredPermissions | none |

---

## Backend

### New file: `WeatherForecastTile.cs`

**Location:** `backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs`

Implements `ITile`. Injects `IWeatherForecastClient`. In `LoadDataAsync`, calls the client, applies the same grouping/hottest-day logic as `GetWeatherForecastHandler`, and returns:

```json
{
  "status": "success",
  "data": {
    "days": [
      {
        "date": "2026-05-19",
        "cityName": "Brno",
        "minTemperatureCelsius": 14.2,
        "maxTemperatureCelsius": 26.0,
        "weatherCode": 1
      }
    ]
  }
}
```

On exception (non-cancellation): log warning, return `{ "status": "error", "error": "Předpověď počasí není dostupná." }`.

### Registration

Add `services.RegisterTile<WeatherForecastTile>();` to `WeatherForecastModule.AddWeatherForecastModule()`.

---

## Frontend

### New file: `WeatherForecastTile.tsx`

**Location:** `frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx`

Receives `data` prop (standard tile data shape). Renders five forecast rows identical in layout to `WeatherForecastReport.tsx`:

```
[date label]  [icon]  [min°]  [color bar]  [max°]  [city]
```

- Uses `getTemperatureColor(maxTemp)` from `temperatureScale.ts` for bar color
- Uses `getTemperatureRangeBar(min, max)` for bar position/width
- Uses `getWeatherIcon(weatherCode)` from `weatherIcons.ts`
- Renders a footer link `→ Zásilky s chlazením` that navigates to `/customer/cooling`
- Error state: standard error message in tile body
- Data shape: `data.data.days` — array of `HottestDayDto`-shaped objects

**Note:** Does not call `useWeatherForecast()` hook directly — data arrives via the dashboard tile data system (fetched by `useTileData()` every 30 s).

### Registration in `TileContent.tsx`

Add a case to the switch statement:

```tsx
case 'weatherforecast':
  return <WeatherForecastTile data={tile.data} />;
```

---

## Data Flow

```
Dashboard auto-refresh (30 s)
  → GET /api/dashboard/data
  → DashboardService (parallel tile load)
  → WeatherForecastTile.LoadDataAsync()
  → IWeatherForecastClient.GetForecastAsync()
  → Returns { status, data: { days: [...] } }
  → Frontend WeatherForecastTile renders rows
```

---

## Error Handling

| Scenario | Backend | Frontend |
|---|---|---|
| Client throws | Log warning, return `status: error` | Show error message in tile body |
| Cancellation | Re-throw (let framework handle) | — |
| Empty days list | Return `status: success`, `days: []` | Render empty state (no rows, subtitle "Žádná data") |

---

## Out of Scope

- No new API endpoint — tile data goes through the existing `/api/dashboard/data` route
- No changes to `WeatherForecastReport.tsx` in the cooling tab
- No new permissions
- No E2E test for this tile (E2E suite targets critical user flows; a dashboard tile is low-risk)
