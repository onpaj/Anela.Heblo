# Weather Forecast Range Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a min→max temperature range bar per forecast day instead of the current 0→max bar, matching the reference design in `.context/attachments/image-v2.png`.

**Architecture:** Backend adds `temperature_2m_min` to the Open-Meteo fetch and threads `MinTemperatureCelsius` through the domain model, adapter, DTO, and handler. Frontend adds `getTemperatureRangeBar` to the existing `temperatureScale.ts` utility, updates the hook type, and rewrites the component row to render a floating segment bar with `5°C ——bar—— 20°C` labels.

**Tech Stack:** .NET 8 (C# records, MediatR), React 18, TypeScript, Tailwind CSS, Jest + React Testing Library

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs` | Add `MinTemperatureCelsius` to domain record |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs` | Fetch `temperature_2m_min`, parse it, pass to domain |
| Modify | `backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs` | Expose `MinTemperatureCelsius` in API response DTO |
| Modify | `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs` | Map `MinTemperatureCelsius` in projection |
| Modify | `frontend/src/api/hooks/useWeatherForecast.ts` | Add `minTemperatureCelsius` to TypeScript interface |
| Modify | `frontend/src/components/customer/cooling/temperatureScale.ts` | Add `TemperatureRangeBar` interface + `getTemperatureRangeBar` function |
| Modify | `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts` | Tests for `getTemperatureRangeBar` |
| Modify | `frontend/src/components/customer/cooling/WeatherForecastReport.tsx` | Render floating range bar with min/max labels |
| Modify | `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx` | Update mock data + assertions for new layout |

---

## Task 1: Backend — add MinTemperatureCelsius throughout the stack

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs`

- [ ] **Step 1: Update `CityForecastDay.cs` — add `MinTemperatureCelsius` to the record**

Replace the entire file content:

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.Weather;

public record CityForecastDay(DateOnly Date, double MinTemperatureCelsius, double MaxTemperatureCelsius, int WeatherCode);
```

- [ ] **Step 2: Update `HottestDayDto.cs` — add `MinTemperatureCelsius` property**

Replace the entire file content (DTOs are classes, not records — project rule):

```csharp
namespace Anela.Heblo.Application.Features.WeatherForecast.Contracts;

public class HottestDayDto
{
    public DateOnly Date { get; set; }
    public string CityName { get; set; } = string.Empty;
    public double MinTemperatureCelsius { get; set; }
    public double MaxTemperatureCelsius { get; set; }
    public int WeatherCode { get; set; }
}
```

- [ ] **Step 3: Update `GetWeatherForecastHandler.cs` — map `MinTemperatureCelsius` in the projection**

The only change is adding one line to the `new HottestDayDto { ... }` initializer. Replace the full `Select` lambda (lines 32–42) with:

```csharp
                .Select(g =>
                {
                    var hottest = g.MaxBy(x => x.Day.MaxTemperatureCelsius)!;
                    return new HottestDayDto
                    {
                        Date = hottest.Day.Date,
                        CityName = hottest.CityName,
                        MinTemperatureCelsius = hottest.Day.MinTemperatureCelsius,
                        MaxTemperatureCelsius = hottest.Day.MaxTemperatureCelsius,
                        WeatherCode = hottest.Day.WeatherCode,
                    };
                })
```

- [ ] **Step 4: Update `OpenMeteoWeatherForecastClient.cs` — fetch and parse min temperature**

Four changes in this file:

**4a. URL string (line 41)** — add `temperature_2m_min` to `daily=`:

```csharp
        var url = $"/v1/forecast?latitude={lats}&longitude={lons}&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=7&timezone=Europe%2FPrague";
```

**4b. Consistency check (lines 62–64)** — add `TemperatureMin` to the length guard:

```csharp
                if (loc.Daily.TemperatureMax.Count != dayCount
                    || loc.Daily.TemperatureMin.Count != dayCount
                    || loc.Daily.WeatherCode.Count != dayCount)
                    throw new InvalidOperationException(
                        $"Open-Meteo daily arrays for '{_options.Cities[i].Name}' have inconsistent lengths");
```

**4c. `CityForecastDay` constructor call (lines 69–72)** — add `MinTemperatureCelsius`:

```csharp
                        .Select((time, j) => new CityForecastDay(
                            Date: DateOnly.Parse(time),
                            MinTemperatureCelsius: loc.Daily.TemperatureMin[j],
                            MaxTemperatureCelsius: loc.Daily.TemperatureMax[j],
                            WeatherCode: loc.Daily.WeatherCode[j]))
```

**4d. `OpenMeteoDailyData` inner class (lines 89–98)** — add `TemperatureMin` property:

```csharp
    private sealed class OpenMeteoDailyData
    {
        [JsonPropertyName("time")]
        public List<string> Time { get; init; } = new();

        [JsonPropertyName("temperature_2m_max")]
        public List<double> TemperatureMax { get; init; } = new();

        [JsonPropertyName("temperature_2m_min")]
        public List<double> TemperatureMin { get; init; } = new();

        [JsonPropertyName("weather_code")]
        public List<int> WeatherCode { get; init; } = new();
    }
```

- [ ] **Step 5: Build and format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && dotnet build 2>&1 | tail -20 && dotnet format 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)` and `Format complete.`

- [ ] **Step 6: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs && git commit -m "feat(cooling): add MinTemperatureCelsius to weather forecast stack"
```

---

## Task 2: Frontend utility — add `getTemperatureRangeBar` (TDD)

**Files:**
- Modify: `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts`
- Modify: `frontend/src/components/customer/cooling/temperatureScale.ts`

- [ ] **Step 1: Write failing tests for `getTemperatureRangeBar`**

Open `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts`.

Add this import to the existing import line at the top (add `getTemperatureRangeBar` to the named imports):

```typescript
import {
  TEMP_SCALE_MIN,
  TEMP_SCALE_MAX,
  COOL_THRESHOLD,
  WARM_THRESHOLD,
  HOT_THRESHOLD,
  VERY_HOT_THRESHOLD,
  getTemperatureBarPercent,
  getTemperatureColor,
  getTemperatureRangeBar,
} from '../temperatureScale';
```

Then append a new `describe` block at the end of the file (after all existing tests):

```typescript
describe('getTemperatureRangeBar', () => {
  it('returns left=0 and width=100 for the full scale range (0–40 °C)', () => {
    const result = getTemperatureRangeBar(TEMP_SCALE_MIN, TEMP_SCALE_MAX);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });

  it('returns correct left and width for a mid-range day (10–30 °C)', () => {
    const result = getTemperatureRangeBar(10, 30);
    expect(result.left).toBe(25);   // (10 - 0) / (40 - 0) * 100
    expect(result.width).toBe(50);  // (30 - 10) / (40 - 0) * 100
  });

  it('clamps both min and max outside the scale (-5–50 °C)', () => {
    const result = getTemperatureRangeBar(-5, 50);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });

  it('returns width=0 when min equals max', () => {
    const result = getTemperatureRangeBar(20, 20);
    expect(result.left).toBe(50);
    expect(result.width).toBe(0);
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL (getTemperatureRangeBar not exported)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="temperatureScale" --watchAll=false 2>&1 | tail -20
```

Expected: Tests fail with `getTemperatureRangeBar is not a function` or similar import error.

- [ ] **Step 3: Implement `getTemperatureRangeBar` in `temperatureScale.ts`**

Add the interface and function at the end of `frontend/src/components/customer/cooling/temperatureScale.ts` (after the existing `getTemperatureColor` function):

```typescript
export interface TemperatureRangeBar {
  left: number;
  width: number;
}

export function getTemperatureRangeBar(min: number, max: number): TemperatureRangeBar {
  const left = getTemperatureBarPercent(min);
  const right = getTemperatureBarPercent(max);
  return { left, width: right - left };
}
```

The full file after this addition:

```typescript
export const TEMP_SCALE_MIN = 0;
export const TEMP_SCALE_MAX = 40;

export const COOL_THRESHOLD = 15;
export const WARM_THRESHOLD = 22;
export const HOT_THRESHOLD = 28;
export const VERY_HOT_THRESHOLD = 34;

export function getTemperatureBarPercent(temp: number): number {
  const clamped = Math.max(TEMP_SCALE_MIN, Math.min(TEMP_SCALE_MAX, temp));
  return ((clamped - TEMP_SCALE_MIN) / (TEMP_SCALE_MAX - TEMP_SCALE_MIN)) * 100;
}

export function getTemperatureColor(temp: number): string {
  if (temp < COOL_THRESHOLD) return 'bg-sky-400';
  if (temp < WARM_THRESHOLD) return 'bg-emerald-400';
  if (temp < HOT_THRESHOLD) return 'bg-amber-400';
  if (temp < VERY_HOT_THRESHOLD) return 'bg-orange-500';
  return 'bg-red-500';
}

export interface TemperatureRangeBar {
  left: number;
  width: number;
}

export function getTemperatureRangeBar(min: number, max: number): TemperatureRangeBar {
  const left = getTemperatureBarPercent(min);
  const right = getTemperatureBarPercent(max);
  return { left, width: right - left };
}
```

- [ ] **Step 4: Run tests — expect all to PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="temperatureScale" --watchAll=false 2>&1 | tail -20
```

Expected: All 14 tests pass (10 existing + 4 new).

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add frontend/src/components/customer/cooling/temperatureScale.ts frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts && git commit -m "feat(cooling): add getTemperatureRangeBar for floating min-max bar"
```

---

## Task 3: Frontend component — update WeatherForecastReport to range bar (TDD)

**Files:**
- Modify: `frontend/src/api/hooks/useWeatherForecast.ts`
- Modify: `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`
- Modify: `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`

- [ ] **Step 1: Update mock data and test assertions in `WeatherForecastReport.test.tsx`**

Replace the entire file with the updated version below. Key changes:
- `mockDays` gains `minTemperatureCelsius` on every entry
- The `'28.5 °C'` / `'30.2 °C'` text assertions update to rounded format (`'29°C'`, `'30°C'`)
- The bar test is updated to check `left` style (non-zero, since min=18°C → left=45%) plus `width` style

```typescript
import React from 'react';
import { render, screen } from '@testing-library/react';
import WeatherForecastReport from '../WeatherForecastReport';
import { useWeatherForecast } from '../../../../api/hooks/useWeatherForecast';

jest.mock('../../../../api/hooks/useWeatherForecast');

const mockDays = [
  { date: '2024-06-01', cityName: 'Praha',   minTemperatureCelsius: 18.0, maxTemperatureCelsius: 28.5, weatherCode: 0 },
  { date: '2024-06-02', cityName: 'Brno',    minTemperatureCelsius: 16.0, maxTemperatureCelsius: 26.5, weatherCode: 3 },
  { date: '2024-06-03', cityName: 'Praha',   minTemperatureCelsius: 20.0, maxTemperatureCelsius: 30.2, weatherCode: 1 },
  { date: '2024-06-04', cityName: 'Ostrava', minTemperatureCelsius: 15.0, maxTemperatureCelsius: 27.0, weatherCode: 45 },
  { date: '2024-06-05', cityName: 'Praha',   minTemperatureCelsius: 14.0, maxTemperatureCelsius: 25.5, weatherCode: 61 },
  { date: '2024-06-06', cityName: 'Brno',    minTemperatureCelsius: 12.0, maxTemperatureCelsius: 24.0, weatherCode: 95 },
  { date: '2024-06-07', cityName: 'Praha',   minTemperatureCelsius: 10.0, maxTemperatureCelsius: 22.0, weatherCode: 71 },
];

describe('WeatherForecastReport', () => {
  it('renders all 7 day rows with min and max temperature labels', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('29°C')).toBeInTheDocument(); // Math.round(28.5)
    expect(screen.getByText('30°C')).toBeInTheDocument(); // Math.round(30.2)
    expect(screen.getByText('18°C')).toBeInTheDocument(); // Math.round(18.0) min for day 1
  });

  it('renders LoadingState when isLoading is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/načítám předpověď/i)).toBeInTheDocument();
  });

  it('renders ErrorState when isError is true', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
    });

    render(<WeatherForecastReport />);

    expect(screen.getByText(/nepodařilo se načíst předpověď/i)).toBeInTheDocument();
  });

  it('renders exactly 7 temperature bars with left offset and width styles', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    const bars = screen.getAllByTestId('temp-bar');
    expect(bars).toHaveLength(7);
    bars.forEach((bar) => {
      expect(bar.getAttribute('style')).toMatch(/left:\s*\d+(\.\d+)?%/);
      expect(bar.getAttribute('style')).toMatch(/width:\s*\d+(\.\d+)?%/);
    });
    // Day 1: min=18°C → left=45% (non-zero confirms it's a range bar, not 0→max)
    expect(bars[0].getAttribute('style')).toMatch(/left:\s*4[0-9]/);
    expect(bars[0]).toHaveClass('bg-orange-500'); // 28.5°C → [28, 34) → orange
  });
});
```

- [ ] **Step 2: Run tests — expect failures (minTemperatureCelsius missing from types, old text assertions broken)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="WeatherForecastReport" --watchAll=false 2>&1 | tail -30
```

Expected: TypeScript errors about `minTemperatureCelsius` not existing on `HottestDayDto`, and the text `'29°C'` not found.

- [ ] **Step 3: Update `useWeatherForecast.ts` — add `minTemperatureCelsius` to the interface**

Replace the entire file:

```typescript
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export interface HottestDayDto {
  date: string;
  cityName: string;
  minTemperatureCelsius: number;
  maxTemperatureCelsius: number;
  weatherCode: number;
}

interface GetWeatherForecastApiResponse {
  success: boolean;
  days: HottestDayDto[];
}

const fetchWeatherForecast = async (): Promise<HottestDayDto[]> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/weather-forecast`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Weather forecast request failed: ${response.status}`);
  }
  const data: GetWeatherForecastApiResponse = await response.json();
  if (!data.success) {
    throw new Error('Weather forecast unavailable');
  }
  return data.days;
};

export function useWeatherForecast() {
  return useQuery({
    queryKey: QUERY_KEYS.weatherForecast,
    queryFn: fetchWeatherForecast,
    staleTime: 30 * 60 * 1000,
  });
}
```

- [ ] **Step 4: Update `WeatherForecastReport.tsx` — render range bar with min/max labels**

Replace the entire file:

```tsx
import { useWeatherForecast } from '../../../api/hooks/useWeatherForecast';
import { getWeatherIcon } from './weatherIcons';
import LoadingState from '../../common/LoadingState';
import ErrorState from '../../common/ErrorState';
import { getTemperatureColor, getTemperatureRangeBar } from './temperatureScale';

function WeatherForecastReport() {
  const { data, isLoading, isError } = useWeatherForecast();

  if (isLoading) {
    return <LoadingState message="Načítám předpověď počasí..." className="h-40" />;
  }

  if (isError || !data) {
    return <ErrorState message="Nepodařilo se načíst předpověď počasí." className="h-40" />;
  }

  return (
    <div className="mx-4 mb-4 rounded-lg border border-gray-200 bg-white p-4">
      <h2 className="mb-3 text-sm font-semibold text-gray-700">
        Předpověď počasí — nejteplejší místo v ČR
      </h2>
      <div className="space-y-2">
        {data.map((day) => {
          const Icon = getWeatherIcon(day.weatherCode);
          const [year, month, dayNum] = day.date.split('-').map(Number);
          const dateObj = new Date(year, month - 1, dayNum);
          const label = dateObj.toLocaleDateString('cs-CZ', {
            weekday: 'short',
            day: 'numeric',
            month: 'numeric',
          });
          const { left, width } = getTemperatureRangeBar(
            day.minTemperatureCelsius,
            day.maxTemperatureCelsius,
          );

          return (
            <div key={day.date} className="flex items-center gap-3 text-sm">
              <span className="w-24 shrink-0 text-gray-500">{label}</span>
              <Icon className="h-4 w-4 shrink-0 text-gray-600" />
              <span className="w-10 shrink-0 text-right text-gray-600">
                {Math.round(day.minTemperatureCelsius)}°C
              </span>
              <div className="relative flex-1 h-2 rounded-full bg-gray-100 overflow-hidden">
                <div
                  data-testid="temp-bar"
                  className={`absolute h-2 ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                  style={{ left: `${left}%`, width: `${width}%` }}
                />
              </div>
              <span className="w-10 shrink-0 text-left font-medium text-gray-900">
                {Math.round(day.maxTemperatureCelsius)}°C
              </span>
              <span className="w-20 shrink-0 text-right text-gray-500">{day.cityName}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default WeatherForecastReport;
```

Note: `getTemperatureBarPercent` is no longer imported directly — the component uses `getTemperatureRangeBar` which handles the math internally. Import line updated accordingly.

- [ ] **Step 5: Run all cooling tests — expect all to PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="cooling" --watchAll=false 2>&1 | tail -30
```

Expected: 3 test suites pass — `temperatureScale.test.ts` (14 tests), `WeatherForecastReport.test.tsx` (4 tests), `weatherIcons.test.ts` (N tests). Total ≥ 27 tests.

- [ ] **Step 6: Lint and build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm run lint 2>&1 | tail -15 && npm run build 2>&1 | tail -15
```

Expected: No lint errors. `Compiled successfully.`

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add frontend/src/api/hooks/useWeatherForecast.ts frontend/src/components/customer/cooling/WeatherForecastReport.tsx frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx && git commit -m "feat(cooling): update forecast to show min-max range bar"
```

---

## Verification Checklist

- [ ] `dotnet build` — 0 errors
- [ ] `dotnet format` — clean
- [ ] `cd frontend && npm run lint` — 0 errors
- [ ] `cd frontend && npm run build` — compiled successfully
- [ ] `cd frontend && npm test -- --testPathPattern="cooling" --watchAll=false` — all tests pass
- [ ] Manual: Chlazení page shows `5°C ——bar—— 20°C` layout per row, bar floating (not starting at left edge), colour matches max temperature heat ramp
