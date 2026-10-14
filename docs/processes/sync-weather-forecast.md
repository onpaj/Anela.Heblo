---
process: sync-weather-forecast
kind: sync
module: weather-forecast
summary: Fetches a 7-day daily forecast for nine Czech cities from Open-Meteo, caches it in memory for 3 hours, and reduces it to the hottest city per day for the Dashboard tile and the expedition cooling tab.
owns:
  - backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/**
  - backend/src/Anela.Heblo.Application/Features/WeatherForecast/**
  - backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/**
  - backend/src/Anela.Heblo.API/Controllers/WeatherForecastController.cs
verified_at: "5e993f9e2"
related:
  - module-weather-forecast
  - calc-dashboard-tiles
---

# Weather forecast (Open-Meteo)

## Purpose
Heat damages cosmetics in transit, so before shipping the expedition team checks how hot it
will be anywhere in the Czech Republic in the next days and decides whether a carrier needs
cooling (chlazení). This process supplies that number: for each of the next 7 days, the
hottest of nine Czech cities, its max/min temperature and a weather icon.

It is shown in two places:
- Dashboard tile **Předpověď počasí** (`weatherforecast`, opt-in, category Manufacture).
- Expedition settings → **Chlazení** tab (`/customer/expedition-settings?tab=cooling`,
  component `WeatherForecastReport`), above the carrier cooling matrix.

The forecast is advisory only: no code reads it to switch cooling on or off. The carrier
cooling matrix on the same tab (`CarrierCoolingController`) is set by hand.

## Trigger
On demand, no Hangfire job. Each call to `GET api/weather-forecast` or each Dashboard refresh
with the weather tile visible asks `IWeatherForecastClient.GetForecastAsync`. Open-Meteo is
called only when the in-memory cache entry `OpenMeteo_Forecast` is missing or expired
(`WeatherForecast:CacheDurationMinutes`, 180 min).

## Data flow
1. Open-Meteo `GET https://api.open-meteo.com/v1/forecast?latitude={9 lats}&longitude={9 lons}
   &daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=7
   &timezone=Europe%2FPrague` — one batch request for all cities; no API key.
2. `OpenMeteoWeatherForecastClient` maps the response array by index to
   `WeatherForecast:Cities[i]` → `CityForecast(CityName, Days[])`, each day
   `(Date, MinTemperatureCelsius, MaxTemperatureCelsius, WeatherCode)`.
3. Result stored in `IMemoryCache` key `OpenMeteo_Forecast` for 180 min (per app instance;
   lost on restart).
4. Reduction (identical in `GetWeatherForecastHandler` and `WeatherForecastTile`): group all
   city-days by date, keep the city with the highest `MaxTemperatureCelsius` per date, order by
   date → list of `HottestDayDto { Date, CityName, MinTemperatureCelsius,
   MaxTemperatureCelsius, WeatherCode }`.
5. Frontend renders one row per day: weekday + date, icon from WMO `weatherCode`
   (`weatherIcons.ts`), min°, a colour bar, max°C rounded, city name.

Nothing is written to the database.

## Logic & formulas
- Temperatures are °C, daily max/min at 2 m, as Open-Meteo returns them (no rounding on the
  backend; the UI rounds to whole degrees).
- Days are local Prague dates (`timezone=Europe/Prague`), today + 6 following days.
- "Hottest" is by **daily maximum**; the min shown is that same city's minimum, not the
  lowest minimum across cities.
- Ties: `MaxBy` keeps the first city in configuration order.
- Validation: the response must contain exactly as many locations as configured cities and
  each city's four daily arrays must be the same length; otherwise the call fails.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `WeatherForecast:Cities` | Praha, Brno, Ostrava, Plzeň, Olomouc, Liberec, České Budějovice, Hradec Králové, Ústí nad Labem (with lat/lon) | Cities queried. Required, at least one — validated at startup. |
| `WeatherForecast:CacheDurationMinutes` | 180 | Cache lifetime of the Open-Meteo result. |
| `WeatherForecast:RequestTimeoutSeconds` | 5 | HTTP timeout for the Open-Meteo call. |

No environment-specific overrides exist in the repo.

## Runtime facts
None.

## Known quirks
- **Title says 5 days, data has 7.** The tile description is "5denní předpověď počasí" but the
  request uses `forecast_days=7` and both screens show all 7 days.
- **Failures are not cached.** While Open-Meteo is down, every Dashboard refresh (every 30 s
  per open page with the tile) retries the call and waits up to 5 s. The tile then shows
  "Předpověď počasí není dostupná."; the endpoint returns error code 2901
  `WeatherForecastUnavailable` and the cooling tab shows "Nepodařilo se načíst předpověď počasí."
- **Startup dependency.** `Cities` is validated on start (`ValidateOnStart`, `MinLength(1)`):
  an empty city list stops the app from booting.
- **Overriding `Cities` from Key Vault/env merges index-wise** with the repo list instead of
  replacing it (memory note `gotcha_config_binder_appends_arrays`): supplying fewer cities
  leaves the remaining repo cities in place.
- The domain interface lives under `Domain/Features/Logistics/Weather` although the feature
  module is `WeatherForecast`.
- No retry/back-off (Polly) on the HTTP client; a single failed call means no data until the
  next request.

## Code entry points
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs` — request URL, response mapping, cache.
- `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/HebloOpenMeteoAdapterModule.cs` — HttpClient, options validation.
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs` — hottest-city reduction for the API.
- `backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs` — same reduction for the tile.
- `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`, `frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx` — the two screens.
