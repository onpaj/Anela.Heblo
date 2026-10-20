---
process: module-weather-forecast
kind: module
module: weather-forecast
summary: Shows which Czech city will be hottest on each of the next 7 days (Open-Meteo), so the expedition team can decide when shipments need cooling.
owns: []
verified_at: "5e993f9e2"
related:
  - sync-weather-forecast
  - module-dashboard
---

# Weather forecast

## Purpose
Cosmetics suffer in summer heat during transport. This small module answers one question for
the expedition (expedice) team: "how hot will it get anywhere in Czechia in the coming days?"
— so they know when to ship with cooling. It shows, per day, the hottest of nine Czech cities
with its max/min temperature and a weather icon. It is information only; it does not change
any shipping or cooling setting by itself.

## Users & screens
- **Expedition staff** — Expedition settings, tab **Chlazení**
  (`/customer/expedition-settings?tab=cooling`; old link `/customer/cooling` redirects there):
  forecast panel "Předpověď počasí — nejteplejší místo v ČR" above the carrier cooling matrix.
- **Any user** — optional Dashboard tile **Předpověď počasí** (`weatherforecast`, Large,
  category Manufacture). Not shown by default; switch it on in Dashboard settings.
- API: `GET api/weather-forecast` → `{ days: HottestDayDto[] }`. No MCP tool.

## Processes
- `sync-weather-forecast` — Open-Meteo daily forecast for nine cities, cached 3 h in memory,
  reduced to the hottest city per day. Trigger: on demand (page view / Dashboard refresh).

No CRUD: the module has no user-editable data. The city list is configuration
(`WeatherForecast:Cities` in `appsettings.json`).

## Data owned
None in the database. One in-memory cache entry, `OpenMeteo_Forecast` (180 min).

## External systems
- **Open-Meteo** (`https://api.open-meteo.com/v1/forecast`), read-only, public API without a
  key: daily `temperature_2m_max`, `temperature_2m_min`, `weather_code`, 7 days,
  `Europe/Prague` time zone.

## Dependencies
- Reads from no other Heblo module.
- Read by: Dashboard (`calc-dashboard-tiles`, tile `weatherforecast`) and the expedition
  settings Cooling tab in the frontend.

## Known quirks
- Tile description says "5denní" (5-day) but 7 days are fetched and shown.
- A failed Open-Meteo call is not cached, so an outage is retried on every refresh (5 s
  timeout each).
- An empty `WeatherForecast:Cities` stops the app from starting (options validated on start).
- The domain interface `IWeatherForecastClient` sits in `Domain/Features/Logistics/Weather`,
  not under a WeatherForecast namespace.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/` — HTTP client, options, cache.
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/` — API handler, Dashboard tile, module registration.
- `backend/src/Anela.Heblo.API/Controllers/WeatherForecastController.cs` — endpoint.
- `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`, `frontend/src/api/hooks/useWeatherForecast.ts` — cooling tab panel (30 min client stale time).
