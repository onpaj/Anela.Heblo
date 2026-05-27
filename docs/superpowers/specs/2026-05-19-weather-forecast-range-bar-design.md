# Weather Forecast Range Bar — Design Spec

**Date:** 2026-05-19
**Branch:** feature/chlazeni-weather-forecast

## Goal

Replace the current single-value heat bar (0 → max temperature) with a range bar that spans from the day's minimum to its maximum temperature, matching the reference design from chmi.cz. Each row shows `5°C ——bar—— 20°C` on a shared 0–40 °C scale so the full temperature range is visually comparable across days.

## Reference

User-provided image: `.context/attachments/image-v2.png`
- Min °C label left of the bar; max °C label right of the bar
- Bar is a coloured segment that starts at the min-temperature position and ends at the max-temperature position on a fixed scale
- Colour reflects the max temperature (heat signal)

---

## Backend Changes

### 1. `CityForecastDay.cs`
`backend/src/Anela.Heblo.Domain/Features/Logistics/Weather/CityForecastDay.cs`

Add `MinTemperatureCelsius` to the record:

```csharp
public record CityForecastDay(DateOnly Date, double MinTemperatureCelsius, double MaxTemperatureCelsius, int WeatherCode);
```

### 2. `OpenMeteoWeatherForecastClient.cs`
`backend/src/Adapters/Anela.Heblo.Adapters.OpenMeteo/OpenMeteoWeatherForecastClient.cs`

- Add `temperature_2m_min` to the API URL query string alongside `temperature_2m_max`
- Add `TemperatureMin` list to `OpenMeteoDailyData` with `[JsonPropertyName("temperature_2m_min")]`
- Validate `TemperatureMin.Count == dayCount` in the consistency check
- Pass `MinTemperatureCelsius: loc.Daily.TemperatureMin[j]` to the `CityForecastDay` constructor

Hottest-city selection stays on `MaxTemperatureCelsius` — unchanged.

### 3. `HottestDayDto.cs`
`backend/src/Anela.Heblo.Application/Features/WeatherForecast/Contracts/HottestDayDto.cs`

Add property (class, not record — per project rules):

```csharp
public double MinTemperatureCelsius { get; set; }
```

### 4. `GetWeatherForecastHandler.cs`
`backend/src/Anela.Heblo.Application/Features/WeatherForecast/UseCases/GetWeatherForecast/GetWeatherForecastHandler.cs`

Add mapping in the `Select` projection:

```csharp
MinTemperatureCelsius = hottest.Day.MinTemperatureCelsius,
```

---

## Frontend Changes

### 5. `useWeatherForecast.ts`
`frontend/src/api/hooks/useWeatherForecast.ts`

Add to `HottestDayDto` interface:

```typescript
minTemperatureCelsius: number;
```

### 6. `temperatureScale.ts`
`frontend/src/components/customer/cooling/temperatureScale.ts`

Add a new exported function and return type:

```typescript
export interface TemperatureRangeBar {
  left: number;   // 0–100, start position on the scale (from min temp)
  width: number;  // 0–100, bar width (max - min, clamped to scale)
}

export function getTemperatureRangeBar(min: number, max: number): TemperatureRangeBar {
  const left = getTemperatureBarPercent(min);
  const right = getTemperatureBarPercent(max);
  return { left, width: right - left };
}
```

This uses the existing `getTemperatureBarPercent` (which already clamps) so no new clamping logic is needed.

### 7. `WeatherForecastReport.tsx`
`frontend/src/components/customer/cooling/WeatherForecastReport.tsx`

Update the per-day row. Replace the current layout with:

```tsx
const { left, width } = getTemperatureRangeBar(day.minTemperatureCelsius, day.maxTemperatureCelsius);

return (
  <div key={day.date} className="flex items-center gap-3 text-sm">
    <span className="w-24 shrink-0 text-gray-500">{label}</span>
    <Icon className="h-4 w-4 shrink-0 text-gray-600" />
    <span className="w-10 shrink-0 text-right text-gray-600">
      {Math.round(day.minTemperatureCelsius)}°C
    </span>
    <div className="flex-1 h-2 rounded-full bg-gray-100 overflow-hidden relative">
      <div
        data-testid="temp-bar"
        className={`absolute h-2 ${getTemperatureColor(day.maxTemperatureCelsius)}`}
        style={{ left: `${left}%`, width: `${width}%` }}
      />
    </div>
    <span className="w-10 shrink-0 text-left text-gray-900 font-medium">
      {Math.round(day.maxTemperatureCelsius)}°C
    </span>
    <span className="w-20 shrink-0 text-right text-gray-500">{day.cityName}</span>
  </div>
);
```

Notes:
- `relative` on the track, `absolute` on the bar — positions the bar within the track using `left` + `width`
- Min temperature rounded (`Math.round`) for a clean label, matching the reference image style
- `data-testid="temp-bar"` retained on the coloured bar div
- City name retained at end

### 8. `WeatherForecastReport.test.tsx`
`frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`

- Add `minTemperatureCelsius` to every entry in `mockDays` (e.g. 18.0 for 28.5 max, etc.)
- Update the existing temperature text assertions: the text format changes from `"28.5 °C"` to `"29°C"` (max) and `"18°C"` (min) — adjust mock values and assertions accordingly
- Update bar test: assert `bars[0]` has both a non-zero `left` style value AND a `width` style value
- Retain color class assertion on `bars[0]`

### 9. `temperatureScale.test.ts`
`frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts`

Add a `describe('getTemperatureRangeBar')` block:

```typescript
describe('getTemperatureRangeBar', () => {
  it('returns left=0 and width=100 for full-scale range (0–40)', () => {
    const result = getTemperatureRangeBar(0, 40);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });

  it('returns correct offsets for a mid-range day (10–30 °C)', () => {
    const result = getTemperatureRangeBar(10, 30);
    expect(result.left).toBe(25);   // 10/40 * 100
    expect(result.width).toBe(50);  // (30-10)/40 * 100
  });

  it('clamps values outside the scale', () => {
    const result = getTemperatureRangeBar(-5, 50);
    expect(result.left).toBe(0);
    expect(result.width).toBe(100);
  });
});
```

---

## Test Mock Data

Updated `mockDays` for the test file (min values chosen to keep assertions clear):

```typescript
const mockDays = [
  { date: '2024-06-01', cityName: 'Praha',   minTemperatureCelsius: 18.0, maxTemperatureCelsius: 28.5, weatherCode: 0 },
  { date: '2024-06-02', cityName: 'Brno',    minTemperatureCelsius: 16.0, maxTemperatureCelsius: 26.5, weatherCode: 3 },
  { date: '2024-06-03', cityName: 'Praha',   minTemperatureCelsius: 20.0, maxTemperatureCelsius: 30.2, weatherCode: 1 },
  { date: '2024-06-04', cityName: 'Ostrava', minTemperatureCelsius: 15.0, maxTemperatureCelsius: 27.0, weatherCode: 45 },
  { date: '2024-06-05', cityName: 'Praha',   minTemperatureCelsius: 14.0, maxTemperatureCelsius: 25.5, weatherCode: 61 },
  { date: '2024-06-06', cityName: 'Brno',    minTemperatureCelsius: 12.0, maxTemperatureCelsius: 24.0, weatherCode: 95 },
  { date: '2024-06-07', cityName: 'Praha',   minTemperatureCelsius: 10.0, maxTemperatureCelsius: 22.0, weatherCode: 71 },
];
```

Temperature text assertions update: existing `"28.5 °C"` → `"29°C"` (max rounded, no space before °C).

---

## Verification

1. `dotnet build` — passes
2. `dotnet format` — clean
3. `cd frontend && npm run build` — passes
4. `cd frontend && npm run lint` — clean
5. `cd frontend && npm test -- --testPathPattern="cooling" --watchAll=false` — all tests pass
6. Manual: Chlazení page shows range bars with min label left, max label right, bar segment starting at min position
