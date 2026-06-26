# Cooling Page — Weather Heatmap & Compact Carrier Matrix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the plain-text weather table with a 7-day horizontal heat-bar comparison, compact the carrier cooling matrix, and rename the "Na ruky" label to "Do ruky".

**Architecture:** Pure frontend change — new `temperatureScale.ts` utility (mirrors `tagColor.ts` style) drives bar width/colour in a reworked `WeatherForecastReport.tsx`; `CarrierCoolingMatrix.tsx` gets spacing tightened and one label renamed. No API, no backend change.

**Tech Stack:** React, TypeScript, Tailwind CSS, Jest + React Testing Library

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `frontend/src/components/customer/cooling/temperatureScale.ts` | Scale constants + two pure functions: bar-percent and colour class |
| Create | `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts` | Unit tests for both functions |
| Modify | `frontend/src/components/customer/cooling/WeatherForecastReport.tsx` | Replace text row with horizontal bar row |
| Modify | `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx` | Add bar-render assertions |
| Modify | `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx` | Spacing reduction + label rename |

---

## Task 1: `temperatureScale.ts` — write failing tests first

**Files:**
- Create: `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts`
- Create: `frontend/src/components/customer/cooling/temperatureScale.ts`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts`:

```typescript
import {
  TEMP_SCALE_MIN,
  TEMP_SCALE_MAX,
  getTemperatureBarPercent,
  getTemperatureColor,
} from '../temperatureScale';

describe('getTemperatureBarPercent', () => {
  it('returns 0 for temperature at or below minimum', () => {
    expect(getTemperatureBarPercent(TEMP_SCALE_MIN)).toBe(0);
    expect(getTemperatureBarPercent(-5)).toBe(0);
  });

  it('returns 100 for temperature at or above maximum', () => {
    expect(getTemperatureBarPercent(TEMP_SCALE_MAX)).toBe(100);
    expect(getTemperatureBarPercent(50)).toBe(100);
  });

  it('returns 50 for the midpoint (20 °C on a 0–40 scale)', () => {
    expect(getTemperatureBarPercent(20)).toBe(50);
  });

  it('returns a proportional value for an arbitrary temperature', () => {
    expect(getTemperatureBarPercent(10)).toBe(25);
    expect(getTemperatureBarPercent(30)).toBe(75);
  });
});

describe('getTemperatureColor', () => {
  it('returns a cool class for temperatures below 15 °C', () => {
    expect(getTemperatureColor(10)).toBe('bg-sky-400');
    expect(getTemperatureColor(0)).toBe('bg-sky-400');
  });

  it('returns a hot class for temperatures above 34 °C', () => {
    expect(getTemperatureColor(35)).toBe('bg-red-500');
    expect(getTemperatureColor(40)).toBe('bg-red-500');
  });

  it('returns different classes for cool vs hot temperatures', () => {
    const cool = getTemperatureColor(5);
    const hot = getTemperatureColor(38);
    expect(cool).not.toBe(hot);
  });
});
```

- [ ] **Step 2: Run tests — expect FAIL (module not found)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="temperatureScale" --watchAll=false 2>&1 | tail -20
```

Expected: `Cannot find module '../temperatureScale'`

- [ ] **Step 3: Implement `temperatureScale.ts`**

Create `frontend/src/components/customer/cooling/temperatureScale.ts`:

```typescript
export const TEMP_SCALE_MIN = 0;
export const TEMP_SCALE_MAX = 40;

const COOL_THRESHOLD = 15;
const WARM_THRESHOLD = 22;
const HOT_THRESHOLD = 28;
const VERY_HOT_THRESHOLD = 34;

export function getTemperatureBarPercent(temp: number): number {
  const clamped = Math.max(TEMP_SCALE_MIN, Math.min(TEMP_SCALE_MAX, temp));
  return (clamped / TEMP_SCALE_MAX) * 100;
}

export function getTemperatureColor(temp: number): string {
  if (temp < COOL_THRESHOLD) return 'bg-sky-400';
  if (temp < WARM_THRESHOLD) return 'bg-emerald-400';
  if (temp < HOT_THRESHOLD) return 'bg-amber-400';
  if (temp < VERY_HOT_THRESHOLD) return 'bg-orange-500';
  return 'bg-red-500';
}
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="temperatureScale" --watchAll=false 2>&1 | tail -20
```

Expected: all 6 tests pass, no failures.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add frontend/src/components/customer/cooling/temperatureScale.ts frontend/src/components/customer/cooling/__tests__/temperatureScale.test.ts && git commit -m "feat(cooling): add temperatureScale utility with bar percent and heat color"
```

---

## Task 2: Rework `WeatherForecastReport.tsx` — horizontal bars

**Files:**
- Modify: `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`
- Modify: `frontend/src/components/customer/cooling/WeatherForecastReport.tsx`

The existing 3 tests check: (a) all rows render with temperature text `"28.5 °C"` / `"30.2 °C"`, (b) `LoadingState`, (c) `ErrorState`. These must stay green.

- [ ] **Step 1: Add the bar-render test to `WeatherForecastReport.test.tsx`**

Open `frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx`.

Append inside the `describe('WeatherForecastReport')` block (after the existing `it('renders ErrorState ...')` test, before the closing `}`):

```typescript
  it('renders exactly 7 temperature bar elements with a width style', () => {
    (useWeatherForecast as jest.Mock).mockReturnValue({
      isLoading: false,
      isError: false,
      data: mockDays,
    });

    render(<WeatherForecastReport />);

    const bars = screen.getAllByTestId('temp-bar');
    expect(bars).toHaveLength(7);
    bars.forEach((bar) => {
      expect(bar).toHaveStyle({ width: expect.stringMatching(/^\d+(\.\d+)?%$/) });
    });
  });
```

- [ ] **Step 2: Run tests — expect the new test to FAIL (temp-bar not found)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="WeatherForecastReport" --watchAll=false 2>&1 | tail -30
```

Expected: 3 tests pass, 1 fails with `Unable to find an element by: [data-testid="temp-bar"]`.

- [ ] **Step 3: Rework the row render in `WeatherForecastReport.tsx`**

Replace the `return (...)` inside the `.map()` callback (currently lines 33–42) with:

```tsx
          return (
            <div key={day.date} className="flex items-center gap-3 text-sm">
              <span className="w-24 shrink-0 text-gray-500">{label}</span>
              <Icon className="h-4 w-4 shrink-0 text-gray-600" />
              <div className="flex-1 h-2 rounded-full bg-gray-100">
                <div
                  data-testid="temp-bar"
                  className={`h-2 rounded-full ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                  style={{ width: `${getTemperatureBarPercent(day.maxTemperatureCelsius)}%` }}
                />
              </div>
              <span className="w-16 shrink-0 text-right font-medium text-gray-900">
                {day.maxTemperatureCelsius.toFixed(1)} °C
              </span>
              <span className="w-20 shrink-0 text-right text-gray-500">{day.cityName}</span>
            </div>
          );
```

Also add the two new imports at the top of the file (after the existing imports):

```typescript
import { getTemperatureBarPercent, getTemperatureColor } from './temperatureScale';
```

The final file should look like:

```tsx
import { useWeatherForecast } from '../../../api/hooks/useWeatherForecast';
import { getWeatherIcon } from './weatherIcons';
import LoadingState from '../../common/LoadingState';
import ErrorState from '../../common/ErrorState';
import { getTemperatureBarPercent, getTemperatureColor } from './temperatureScale';

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

          return (
            <div key={day.date} className="flex items-center gap-3 text-sm">
              <span className="w-24 shrink-0 text-gray-500">{label}</span>
              <Icon className="h-4 w-4 shrink-0 text-gray-600" />
              <div className="flex-1 h-2 rounded-full bg-gray-100">
                <div
                  data-testid="temp-bar"
                  className={`h-2 rounded-full ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                  style={{ width: `${getTemperatureBarPercent(day.maxTemperatureCelsius)}%` }}
                />
              </div>
              <span className="w-16 shrink-0 text-right font-medium text-gray-900">
                {day.maxTemperatureCelsius.toFixed(1)} °C
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

- [ ] **Step 4: Run all cooling tests — expect all 4 to PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="cooling" --watchAll=false 2>&1 | tail -30
```

Expected: all tests pass (temperatureScale × 6, WeatherForecastReport × 4, weatherIcons × N).

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add frontend/src/components/customer/cooling/WeatherForecastReport.tsx frontend/src/components/customer/cooling/__tests__/WeatherForecastReport.test.tsx && git commit -m "feat(cooling): replace forecast text table with horizontal heat bars"
```

---

## Task 3: Compact `CarrierCoolingMatrix.tsx` and rename label

**Files:**
- Modify: `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`

No new tests needed — this is a purely visual spacing change and a copy-string rename. The existing component has no unit tests; lint + build are the gates.

- [ ] **Step 1: Apply spacing reductions and rename**

In `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx` make the following targeted replacements:

**Line 24** — rename the label:
```typescript
// Before:
  NaRuky: 'Na ruky',
// After:
  NaRuky: 'Do ruky',
```

**Line 36** — outer wrapper:
```tsx
// Before:
    <div className="space-y-4 p-4">
// After:
    <div className="space-y-3 p-4">
```

**Line 42** — card header:
```tsx
// Before:
          <div className="px-4 py-3 border-b border-gray-100 bg-gray-50">
// After:
          <div className="px-3 py-2 border-b border-gray-100 bg-gray-50">
```

**Line 59** — row:
```tsx
// Before:
                  className="flex items-center px-4 py-3 gap-6"
// After:
                  className="flex items-center px-3 py-2 gap-4"
```

**Line 61** — handling label width:
```tsx
// Before:
                  <span className="w-24 text-sm text-gray-700 flex-shrink-0">
// After:
                  <span className="w-20 text-sm text-gray-700 flex-shrink-0">
```

**Line 64** — cooling options group:
```tsx
// Before:
                  <div className="flex gap-6">
// After:
                  <div className="flex gap-4">
```

After all edits the full file should be:

```tsx
import {
  Carriers,
  CarrierGroupDto,
  Cooling,
  DeliveryHandling,
  SetCarrierCoolingRequest,
} from '../../../api/hooks/useCarrierCooling';

interface CarrierCoolingMatrixProps {
  groups: CarrierGroupDto[];
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
  savingRow: { carrier: Carriers; deliveryHandling: DeliveryHandling } | null;
}

const CARRIER_LABELS: Record<Carriers, string> = {
  Zasilkovna: 'Zásilkovna',
  PPL: 'PPL',
  GLS: 'GLS',
  Osobak: 'Osobní odběr',
};

const HANDLING_LABELS: Record<DeliveryHandling, string> = {
  NaRuky: 'Do ruky',
  Box: 'Box',
};

const COOLING_OPTIONS: { value: Cooling; label: string }[] = [
  { value: 'None', label: 'Bez chlazení' },
  { value: 'L1', label: 'L1' },
  { value: 'L2', label: 'L2' },
];

function CarrierCoolingMatrix({ groups, onSetCooling, isSaving, savingRow }: CarrierCoolingMatrixProps) {
  return (
    <div className="space-y-3 p-4">
      {groups.map((group) => (
        <div
          key={group.carrier}
          className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
        >
          <div className="px-3 py-2 border-b border-gray-100 bg-gray-50">
            <h2 className="text-sm font-semibold text-gray-800">
              {CARRIER_LABELS[group.carrier] ?? `Dopravce ${group.carrier}`}
            </h2>
          </div>
          <div className="divide-y divide-gray-50">
            {group.rows.map((row) => {
              const radioName = `${group.carrier}-${row.deliveryHandling}`;

              const isThisRowSaving =
                isSaving &&
                savingRow?.carrier === group.carrier &&
                savingRow?.deliveryHandling === row.deliveryHandling;

              return (
                <div
                  key={row.deliveryHandling}
                  className="flex items-center px-3 py-2 gap-4"
                >
                  <span className="w-20 text-sm text-gray-700 flex-shrink-0">
                    {HANDLING_LABELS[row.deliveryHandling] ?? String(row.deliveryHandling)}
                  </span>
                  <div className="flex gap-4">
                    {COOLING_OPTIONS.map((option) => (
                      <label
                        key={option.value}
                        className="flex items-center gap-2 cursor-pointer"
                      >
                        <input
                          type="radio"
                          name={radioName}
                          value={option.value}
                          checked={row.cooling === option.value}
                          onChange={() =>
                            onSetCooling({
                              carrier: group.carrier,
                              deliveryHandling: row.deliveryHandling,
                              cooling: option.value,
                            })
                          }
                          disabled={isSaving}
                          className="h-4 w-4 text-indigo-600 cursor-pointer"
                        />
                        <span className="text-sm text-gray-700">{option.label}</span>
                      </label>
                    ))}
                  </div>
                  {isThisRowSaving && (
                    <span className="text-xs text-gray-400 ml-2 animate-pulse">
                      Ukládám…
                    </span>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}

export default CarrierCoolingMatrix;
```

- [ ] **Step 2: Run all cooling tests + lint + build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado/frontend && npm test -- --testPathPattern="cooling" --watchAll=false 2>&1 | tail -20 && npm run lint 2>&1 | tail -20 && npm run build 2>&1 | tail -20
```

Expected: all tests pass, lint clean, build succeeds.

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/manado && git add frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx && git commit -m "fix(cooling): compact carrier matrix spacing and rename Na ruky to Do ruky"
```

---

## Verification Checklist

- [ ] `cd frontend && npm run lint` — passes with no errors
- [ ] `cd frontend && npm run build` — passes with no errors
- [ ] `cd frontend && npm test -- --testPathPattern="cooling" --watchAll=false` — all tests pass
- [ ] Manual: open Chlazení page — 7 horizontal heat bars visible, coloured cool→hot, with weather icon + city name per row
- [ ] Manual: carrier matrix is visibly tighter; delivery rows show "Do ruky" not "Na ruky"
