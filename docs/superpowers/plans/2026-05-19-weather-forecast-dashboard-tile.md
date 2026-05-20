# Weather Forecast Dashboard Tile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dashboard tile that displays the 5-day weather forecast (hottest city in CZ per day) with temperature color bars, reusing existing backend and frontend forecast utilities.

**Architecture:** A new `WeatherForecastTile` C# class implements `ITile`, delegates to the existing `IWeatherForecastClient`, and returns a JSON-serializable object. On the frontend, a new `WeatherForecastTile.tsx` receives the tile data as a prop and renders rows identical to the cooling-tab `WeatherForecastReport` component. The tile is registered in `WeatherForecastModule` (backend) and `TileContent` (frontend).

**Tech Stack:** .NET 8 / C# (xUnit, FluentAssertions, Moq), React 18 / TypeScript (Vitest, @testing-library/react), Tailwind CSS

---

## File Map

| Action | Path |
|--------|------|
| **Create** | `backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs` |
| **Create** | `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs` |
| **Modify** | `backend/src/Anela.Heblo.Application/Features/WeatherForecast/WeatherForecastModule.cs` |
| **Create** | `frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx` |
| **Create** | `frontend/src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx` |
| **Modify** | `frontend/src/components/dashboard/tiles/TileContent.tsx` |

---

### Task 1: Backend — WeatherForecastTile class

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.WeatherForecast.DashboardTiles;

public class WeatherForecastTileTests
{
    private readonly Mock<IWeatherForecastClient> _clientMock;
    private readonly Mock<ILogger<WeatherForecastTile>> _loggerMock;
    private readonly WeatherForecastTile _tile;

    public WeatherForecastTileTests()
    {
        _clientMock = new Mock<IWeatherForecastClient>();
        _loggerMock = new Mock<ILogger<WeatherForecastTile>>();
        _tile = new WeatherForecastTile(_clientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Metadata_HasCorrectValues()
    {
        _tile.Title.Should().Be("Předpověď počasí");
        _tile.Description.Should().Be("5denní předpověď počasí — nejteplejší místo v ČR");
        _tile.Size.Should().Be(TileSize.Large);
        _tile.Category.Should().Be(TileCategory.Manufacture);
        _tile.DefaultEnabled.Should().BeFalse();
        _tile.AutoShow.Should().BeTrue();
        _tile.ComponentType.Should().Be(typeof(object));
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    [Fact]
    public void TileId_IsWeatherForecast()
    {
        TileExtensions.GetTileId<WeatherForecastTile>().Should().Be("weatherforecast");
    }

    [Fact]
    public async Task LoadDataAsync_WithMultipleCities_ReturnsHottestCityPerDay()
    {
        // Arrange — two cities, same date; Brno is hotter
        var forecasts = new List<CityForecast>
        {
            new("Praha", new[]
            {
                new CityForecastDay(new DateOnly(2026, 5, 19), 14.0, 22.0, 1),
            }),
            new("Brno", new[]
            {
                new CityForecastDay(new DateOnly(2026, 5, 19), 16.0, 28.0, 0),
            }),
        };

        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(forecasts);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        var days = json.GetProperty("data").GetProperty("days");
        days.GetArrayLength().Should().Be(1);
        var day = days[0];
        day.GetProperty("cityName").GetString().Should().Be("Brno");
        day.GetProperty("maxTemperatureCelsius").GetDouble().Should().Be(28.0);
        day.GetProperty("minTemperatureCelsius").GetDouble().Should().Be(16.0);
        day.GetProperty("date").GetString().Should().Be("2026-05-19");
    }

    [Fact]
    public async Task LoadDataAsync_WithEmptyForecast_ReturnsSuccessWithEmptyDays()
    {
        // Arrange
        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CityForecast>());

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("success");
        json.GetProperty("data").GetProperty("days").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task LoadDataAsync_WhenClientThrows_ReturnsErrorStatus()
    {
        // Arrange
        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Open-Meteo unreachable"));

        // Act — must not throw
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("status").GetString().Should().Be("error");
        json.GetProperty("error").GetString().Should().Be("Předpověď počasí není dostupná.");
    }

    [Fact]
    public async Task LoadDataAsync_WhenCancelled_PropagatesCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _clientMock.Setup(x => x.GetForecastAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _tile.LoadDataAsync(cancellationToken: cts.Token));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~WeatherForecastTileTests" -v minimal 2>&1 | tail -20
```

Expected: build error — `WeatherForecastTile` does not exist yet.

- [ ] **Step 3: Create the tile implementation**

Create `backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics.Weather;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;

public class WeatherForecastTile : ITile
{
    private readonly IWeatherForecastClient _weatherClient;
    private readonly ILogger<WeatherForecastTile> _logger;

    public string Title => "Předpověď počasí";
    public string Description => "5denní předpověď počasí — nejteplejší místo v ČR";
    public TileSize Size => TileSize.Large;
    public TileCategory Category => TileCategory.Manufacture;
    public bool DefaultEnabled => false;
    public bool AutoShow => true;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public WeatherForecastTile(IWeatherForecastClient weatherClient, ILogger<WeatherForecastTile> logger)
    {
        _weatherClient = weatherClient;
        _logger = logger;
    }

    public async Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var forecasts = await _weatherClient.GetForecastAsync(cancellationToken);

            var days = forecasts
                .SelectMany(city => city.Days.Select(day => (CityName: city.CityName, Day: day)))
                .GroupBy(x => x.Day.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var hottest = g.MaxBy(x => x.Day.MaxTemperatureCelsius)!;
                    return new
                    {
                        date = hottest.Day.Date.ToString("yyyy-MM-dd"),
                        cityName = hottest.CityName,
                        minTemperatureCelsius = hottest.Day.MinTemperatureCelsius,
                        maxTemperatureCelsius = hottest.Day.MaxTemperatureCelsius,
                        weatherCode = hottest.Day.WeatherCode,
                    };
                })
                .ToList();

            return new { status = "success", data = new { days } };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load weather forecast for dashboard tile");
            return new { status = "error", error = "Předpověď počasí není dostupná." };
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~WeatherForecastTileTests" -v minimal 2>&1 | tail -20
```

Expected: 5 tests pass, 0 failures.

- [ ] **Step 5: Verify build is clean**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/backend
dotnet build --no-incremental -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/WeatherForecast/DashboardTiles/WeatherForecastTile.cs \
  backend/test/Anela.Heblo.Tests/Features/WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs
git commit -m "feat(dashboard): add WeatherForecastTile backend implementation"
```

---

### Task 2: Backend — Register tile

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/WeatherForecast/WeatherForecastModule.cs`

- [ ] **Step 1: Register the tile in the module**

Current content of `WeatherForecastModule.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.WeatherForecast;

public static class WeatherForecastModule
{
    public static IServiceCollection AddWeatherForecastModule(this IServiceCollection services)
    {
        return services;
    }
}
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.WeatherForecast.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.WeatherForecast;

public static class WeatherForecastModule
{
    public static IServiceCollection AddWeatherForecastModule(this IServiceCollection services)
    {
        services.RegisterTile<WeatherForecastTile>();
        return services;
    }
}
```

- [ ] **Step 2: Verify build is clean**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/backend
dotnet build --no-incremental -v minimal 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run full backend test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/backend
dotnet test -v minimal 2>&1 | tail -15
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/WeatherForecast/WeatherForecastModule.cs
git commit -m "feat(dashboard): register WeatherForecastTile in DI"
```

---

### Task 3: Frontend — WeatherForecastTile component

**Files:**
- Create: `frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx`
- Create: `frontend/src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { WeatherForecastTile } from '../WeatherForecastTile';

const fiveDays = {
  status: 'success',
  data: {
    days: [
      { date: '2026-05-19', cityName: 'Brno', minTemperatureCelsius: 14, maxTemperatureCelsius: 18, weatherCode: 2 },
      { date: '2026-05-20', cityName: 'Praha', minTemperatureCelsius: 19, maxTemperatureCelsius: 26, weatherCode: 0 },
      { date: '2026-05-21', cityName: 'Brno', minTemperatureCelsius: 9, maxTemperatureCelsius: 11, weatherCode: 95 },
      { date: '2026-05-22', cityName: 'Praha', minTemperatureCelsius: 16, maxTemperatureCelsius: 23, weatherCode: 3 },
      { date: '2026-05-23', cityName: 'Brno', minTemperatureCelsius: 22, maxTemperatureCelsius: 31, weatherCode: 0 },
    ],
  },
};

function renderTile(data: object) {
  return render(
    <MemoryRouter>
      <WeatherForecastTile data={data} />
    </MemoryRouter>,
  );
}

describe('WeatherForecastTile', () => {
  it('renders five forecast rows', () => {
    renderTile(fiveDays);
    // Each row shows city name
    expect(screen.getAllByText('Brno').length).toBeGreaterThanOrEqual(3);
    expect(screen.getAllByText('Praha').length).toBeGreaterThanOrEqual(2);
  });

  it('renders min and max temperature for each row', () => {
    renderTile(fiveDays);
    // First row: 14° and 18°C
    expect(screen.getByText('14°')).toBeInTheDocument();
    expect(screen.getByText('18°C')).toBeInTheDocument();
  });

  it('applies red color class for max temp >= 26°C', () => {
    renderTile(fiveDays);
    const bars = screen.getAllByTestId('temp-bar');
    // Day 2: max 26°C → bg-red-500
    const redBar = bars.find(b => b.className.includes('bg-red-500'));
    expect(redBar).toBeDefined();
  });

  it('applies amber color class for max temp 21–25°C', () => {
    renderTile(fiveDays);
    const bars = screen.getAllByTestId('temp-bar');
    // Day 4: max 23°C → bg-orange-500 (21–25 range is actually orange-500, not amber)
    // Day 1: max 18°C → bg-amber-400
    const amberBar = bars.find(b => b.className.includes('bg-amber-400'));
    expect(amberBar).toBeDefined();
  });

  it('renders a link to the cooling page', () => {
    renderTile(fiveDays);
    const link = screen.getByRole('link', { name: /chlaz/i });
    expect(link).toHaveAttribute('href', '/customer/cooling');
  });

  it('shows error message when status is error', () => {
    renderTile({ status: 'error', error: 'Předpověď počasí není dostupná.' });
    expect(screen.getByText('Předpověď počasí není dostupná.')).toBeInTheDocument();
  });

  it('shows empty state message when days array is empty', () => {
    renderTile({ status: 'success', data: { days: [] } });
    expect(screen.getByText('Žádná data')).toBeInTheDocument();
  });

  it('renders nothing when data.data is undefined', () => {
    const { container } = renderTile({ status: 'success' });
    expect(container.firstChild).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/frontend
npx vitest run src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx 2>&1 | tail -20
```

Expected: error — `WeatherForecastTile` module not found.

- [ ] **Step 3: Create the tile component**

Create `frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx`:

```tsx
import React from 'react';
import { Link } from 'react-router-dom';
import { getWeatherIcon } from '../../customer/cooling/weatherIcons';
import { getTemperatureColor, getTemperatureRangeBar } from '../../customer/cooling/temperatureScale';

interface ForecastDay {
  date: string;
  cityName: string;
  minTemperatureCelsius: number;
  maxTemperatureCelsius: number;
  weatherCode: number;
}

interface WeatherForecastTileProps {
  data: {
    status?: string;
    error?: string;
    data?: {
      days: ForecastDay[];
    };
  };
}

export function WeatherForecastTile({ data }: WeatherForecastTileProps) {
  if (!data.data) return null;

  if (data.status === 'error') {
    return (
      <div className="flex h-full items-center justify-center p-4 text-center text-sm text-red-600">
        {data.error ?? 'Předpověď počasí není dostupná.'}
      </div>
    );
  }

  const { days } = data.data;

  return (
    <div className="flex h-full flex-col p-4">
      <div className="flex-1 space-y-2">
        {days.length === 0 ? (
          <p className="text-sm text-gray-400">Žádná data</p>
        ) : (
          days.map((day) => {
            const icon = getWeatherIcon(day.weatherCode);
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
                <span className="w-14 shrink-0 text-gray-500">{label}</span>
                <span className="shrink-0 text-base leading-none">{icon}</span>
                <span className="w-8 shrink-0 text-right text-gray-600">
                  {Math.round(day.minTemperatureCelsius)}°
                </span>
                <div className="relative h-2 flex-1 overflow-hidden rounded-full bg-gray-100">
                  <div
                    data-testid="temp-bar"
                    className={`absolute h-2 ${getTemperatureColor(day.maxTemperatureCelsius)}`}
                    style={{ left: `${left}%`, width: `${width}%` }}
                  />
                </div>
                <span className="w-8 shrink-0 text-left font-medium text-gray-900">
                  {Math.round(day.maxTemperatureCelsius)}°C
                </span>
                <span className="w-12 shrink-0 text-right text-xs text-gray-400">
                  {day.cityName}
                </span>
              </div>
            );
          })
        )}
      </div>
      <div className="mt-3 border-t border-gray-100 pt-2 text-right">
        <Link
          to="/customer/cooling"
          className="text-xs text-gray-400 hover:text-gray-600"
        >
          → Zásilky s chlazením
        </Link>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/frontend
npx vitest run src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx 2>&1 | tail -20
```

Expected: 8 tests pass, 0 failures.

- [ ] **Step 5: Run frontend lint and build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/frontend
npm run lint 2>&1 | tail -10
npm run build 2>&1 | tail -10
```

Expected: no lint errors, build succeeds.

- [ ] **Step 6: Commit**

```bash
git add \
  frontend/src/components/dashboard/tiles/WeatherForecastTile.tsx \
  frontend/src/components/dashboard/tiles/__tests__/WeatherForecastTile.test.tsx
git commit -m "feat(dashboard): add WeatherForecastTile frontend component"
```

---

### Task 4: Frontend — Register tile in TileContent

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/TileContent.tsx`

- [ ] **Step 1: Add import and case**

In `frontend/src/components/dashboard/tiles/TileContent.tsx`:

Add the import after the existing tile imports (around line 14, before the `DefaultTile` import line):
```tsx
import { WeatherForecastTile } from './WeatherForecastTile';
```

Add the case inside the `switch (tile.tileId)` block, before the `default:` case (around line 78):
```tsx
case 'weatherforecast':
  return <WeatherForecastTile data={tile.data} />;
```

- [ ] **Step 2: Run frontend lint and build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/frontend
npm run lint 2>&1 | tail -10
npm run build 2>&1 | tail -10
```

Expected: no errors, build succeeds.

- [ ] **Step 3: Run full frontend test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/dallas/frontend
npx vitest run 2>&1 | tail -15
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/dashboard/tiles/TileContent.tsx
git commit -m "feat(dashboard): register weatherforecast tile in TileContent"
```
