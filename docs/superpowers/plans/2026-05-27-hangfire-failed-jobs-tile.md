# Hangfire Failed Jobs Dashboard Tile — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small, always-red dashboard tile that shows the count of Hangfire failed jobs and links to `/hangfire/jobs/failed`.

**Architecture:** `FailedJobsTile` (backend) implements `ITile`, receives an injected `JobStorage`, and calls `GetMonitoringApi().FailedCount()` wrapped in a try/catch that returns a structured error envelope. The frontend `FailedJobsTile.tsx` mirrors `DataQualityTile.tsx` — always-red alert styling, `window.location.href` for navigation (not React Router, because Hangfire is server-rendered). Wired into `TileContent.tsx` under the key `'failedjobs'`.

**Tech Stack:** .NET 8, Hangfire.Core (already in Application.csproj), xUnit + Moq + FluentAssertions; React 18, Lucide-react, React Testing Library + jest.

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| **Create** | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` | `ITile` implementation — loads failed count from Hangfire |
| **Create** | `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` | Unit tests: zero failures, >0 failures, monitoring API throws, metadata |
| **Modify** | `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` | Register `JobStorage` singleton + `RegisterTile<FailedJobsTile>()` |
| **Create** | `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` | React tile component — always-red, click → `/hangfire/jobs/failed` |
| **Create** | `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx` | FE unit tests: error state, zero count, non-zero count, click navigation |
| **Modify** | `frontend/src/components/dashboard/tiles/TileContent.tsx` | Add `case 'failedjobs': return <FailedJobsTile data={tile.data} />;` |

**Tile ID derivation (from `TileExtensions.cs`):**
```
typeof(FailedJobsTile).Name.ToLowerInvariant().Replace("tile", "")
  = "failedjobstile".Replace("tile", "")
  = "failedjobs"
```
→ Use `'failedjobs'` in the `TileContent.tsx` switch case.

---

## Task 1: Backend — FailedJobsTile (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`
- Create (test first): `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs`

---

- [ ] **Step 1.1: Write the failing test file**

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.DashboardTiles;

public class FailedJobsTileTests
{
    private readonly Mock<JobStorage> _storageMock = new();
    private readonly Mock<IMonitoringApi> _monitoringApiMock = new();
    private readonly FailedJobsTile _tile;

    public FailedJobsTileTests()
    {
        _storageMock.Setup(s => s.GetMonitoringApi()).Returns(_monitoringApiMock.Object);
        _tile = new FailedJobsTile(_storageMock.Object, NullLogger<FailedJobsTile>.Instance);
    }

    [Fact]
    public async Task LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(0L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(0L);
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
        doc.RootElement.GetProperty("drillDown").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Returns(7L);

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("success");
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt64().Should().Be(7L);
    }

    [Fact]
    public async Task LoadDataAsync_MonitoringApiThrows_ReturnsErrorAndDoesNotPropagate()
    {
        _monitoringApiMock.Setup(a => a.FailedCount()).Throws(new InvalidOperationException("storage unavailable"));

        var result = await _tile.LoadDataAsync();

        var doc = ToJsonDoc(result);
        doc.RootElement.GetProperty("status").GetString().Should().Be("error");
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("error").GetString().Should().Be("storage unavailable");
        doc.RootElement.GetProperty("drillDown").GetProperty("url").GetString()
            .Should().Be("/hangfire/jobs/failed");
    }

    [Fact]
    public void TileMetadata_MatchesSpec()
    {
        _tile.Title.Should().Be("Failed background jobs");
        _tile.Description.Should().Be("Hangfire jobs in the failed queue");
        _tile.Size.Should().Be(TileSize.Small);
        _tile.Category.Should().Be(TileCategory.System);
        _tile.DefaultEnabled.Should().BeTrue();
        _tile.AutoShow.Should().BeFalse();
        _tile.RequiredPermissions.Should().BeEmpty();
    }

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
```

- [ ] **Step 1.2: Run the tests — verify they FAIL (type not found)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FailedJobsTileTests" \
  --no-build 2>&1 | tail -20
```

Expected: build error — `FailedJobsTile` not found.

- [ ] **Step 1.3: Create the implementation**

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`:

```csharp
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;

public class FailedJobsTile : ITile
{
    private readonly JobStorage _jobStorage;
    private readonly ILogger<FailedJobsTile> _logger;

    public string Title => "Failed background jobs";
    public string Description => "Hangfire jobs in the failed queue";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => false;
    public Type ComponentType => typeof(object);
    public string[] RequiredPermissions => Array.Empty<string>();

    public FailedJobsTile(JobStorage jobStorage, ILogger<FailedJobsTile> logger)
    {
        _jobStorage = jobStorage;
        _logger = logger;
    }

    public Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var failedCount = _jobStorage.GetMonitoringApi().FailedCount();

            return Task.FromResult<object>(new
            {
                status = "success",
                data = new { count = failedCount },
                metadata = new { lastUpdated = DateTime.UtcNow, source = "Hangfire" },
                drillDown = new
                {
                    url = "/hangfire/jobs/failed",
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Hangfire failed job count");

            return Task.FromResult<object>(new
            {
                status = "error",
                data = (object?)null,
                error = ex.Message,
                drillDown = new
                {
                    url = "/hangfire/jobs/failed",
                    enabled = true,
                    tooltip = "Open Hangfire failed jobs"
                }
            });
        }
    }
}
```

- [ ] **Step 1.4: Run the tests — verify they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FailedJobsTileTests" 2>&1 | tail -20
```

Expected: `4 passed, 0 failed`.

- [ ] **Step 1.5: Backend build gate**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
dotnet build backend/Anela.Heblo.sln -c Release --no-restore 2>&1 | tail -10
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | tail -5
```

Expected: `Build succeeded`, `No changes found`.

- [ ] **Step 1.6: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs
git commit -m "feat(dashboard): add FailedJobsTile backend implementation"
```

---

## Task 2: Backend — DI Registration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`

---

- [ ] **Step 2.1: Add registration to DashboardModule**

Open `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`. The current content is:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();

        return services;
    }
}
```

Replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();
        services.RegisterTile<FailedJobsTile>();

        return services;
    }
}
```

- [ ] **Step 2.2: Build gate**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
dotnet build backend/Anela.Heblo.sln -c Release --no-restore 2>&1 | tail -10
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | tail -5
```

Expected: `Build succeeded`, `No changes found`.

- [ ] **Step 2.3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
git add backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
git commit -m "feat(dashboard): register FailedJobsTile in DashboardModule"
```

---

## Task 3: Frontend — FailedJobsTile component (TDD)

**Files:**
- Create (test first): `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`
- Create: `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx`

---

- [ ] **Step 3.1: Write the failing test file**

Create `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FailedJobsTile } from '../FailedJobsTile';

// window.location.href is read-only in jsdom; replace with a writable stub
const originalLocation = window.location;
beforeAll(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { href: '' },
  });
});
afterAll(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: originalLocation,
  });
});
beforeEach(() => {
  (window.location as { href: string }).href = '';
});

describe('FailedJobsTile', () => {
  it('renders error state without a clickable wrapper', () => {
    render(<FailedJobsTile data={{ status: 'error', error: 'storage unavailable' }} />);

    expect(screen.getByText('Unavailable')).toBeInTheDocument();
    expect(screen.queryByTestId('failed-jobs-tile')).toBeNull();
  });

  it('renders count 0 with red styling', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 0 } }} />);

    expect(screen.getByTestId('failed-jobs-tile')).toBeInTheDocument();
    expect(screen.getByText('0')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('renders non-zero count', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 12 } }} />);

    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('failed jobs')).toBeInTheDocument();
  });

  it('navigates to /hangfire/jobs/failed on click', () => {
    render(<FailedJobsTile data={{ status: 'success', data: { count: 3 } }} />);

    fireEvent.click(screen.getByTestId('failed-jobs-tile'));

    expect(window.location.href).toBe('/hangfire/jobs/failed');
  });
});
```

- [ ] **Step 3.2: Run the tests — verify they FAIL (module not found)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia/frontend
npm test -- --testPathPattern="FailedJobsTile" --watchAll=false 2>&1 | tail -20
```

Expected: `Cannot find module '../FailedJobsTile'`.

- [ ] **Step 3.3: Create the component**

Create `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx`:

```tsx
import React from 'react';
import { AlertTriangle, XCircle } from 'lucide-react';

interface FailedJobsTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
    };
    error?: string;
  };
}

export const FailedJobsTile: React.FC<FailedJobsTileProps> = ({ data }) => {
  const handleClick = () => {
    window.location.href = '/hangfire/jobs/failed';
  };

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
          <p className="text-red-600 text-sm">Unavailable</p>
        </div>
      </div>
    );
  }

  const count = data.data?.count ?? 0;

  return (
    <div
      data-testid="failed-jobs-tile"
      className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
      onClick={handleClick}
      style={{ touchAction: 'manipulation' }}
    >
      <div className="mb-2 text-red-600">
        <AlertTriangle className="h-10 w-10" />
      </div>
      <div className="text-3xl font-bold mb-1 text-red-700">
        {count}
      </div>
      <div className="text-sm text-gray-500">
        failed jobs
      </div>
    </div>
  );
};
```

- [ ] **Step 3.4: Run the tests — verify they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia/frontend
npm test -- --testPathPattern="FailedJobsTile" --watchAll=false 2>&1 | tail -20
```

Expected: `4 passed, 0 failed`.

- [ ] **Step 3.5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
git add frontend/src/components/dashboard/tiles/FailedJobsTile.tsx \
        frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx
git commit -m "feat(dashboard): add FailedJobsTile frontend component"
```

---

## Task 4: Frontend — Wire TileContent + Build Gate

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/TileContent.tsx`

---

- [ ] **Step 4.1: Add the import and switch case to TileContent.tsx**

In `frontend/src/components/dashboard/tiles/TileContent.tsx`, make two changes:

**Add import** (after the existing `DqtYesterdayStatusTile` import on line 13):
```tsx
import { FailedJobsTile } from './FailedJobsTile';
```

**Add switch case** (after the `'weatherforecast'` case before `default`):
```tsx
    case 'failedjobs':
      return <FailedJobsTile data={tile.data} />;
```

The relevant section of the file after your edits should look like:

```tsx
import { DqtYesterdayStatusTile } from './DqtYesterdayStatusTile';
import { WeatherForecastTile } from './WeatherForecastTile';
import { FailedJobsTile } from './FailedJobsTile';
import { DefaultTile } from './DefaultTile';
```

And in the switch:

```tsx
    case 'weatherforecast':
      return <WeatherForecastTile data={tile.data} />;
    case 'failedjobs':
      return <FailedJobsTile data={tile.data} />;
    default:
      return <DefaultTile data={tile.data} />;
```

- [ ] **Step 4.2: Frontend build gate**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia/frontend
npm run build 2>&1 | tail -15
npm run lint 2>&1 | tail -10
```

Expected: build completes with no errors, lint is clean.

- [ ] **Step 4.3: Run all tile tests to verify nothing regressed**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia/frontend
npm test -- --testPathPattern="tiles/__tests__" --watchAll=false 2>&1 | tail -20
```

Expected: all tile tests pass.

- [ ] **Step 4.4: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/sofia
git add frontend/src/components/dashboard/tiles/TileContent.tsx
git commit -m "feat(dashboard): wire FailedJobsTile into TileContent"
```

---

## Verification Checklist

After all tasks are complete, run through this manually:

- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FailedJobsTileTests"` → 4 passed
- [ ] `npm test -- --testPathPattern="FailedJobsTile" --watchAll=false` → 4 passed
- [ ] `dotnet build` → green
- [ ] `dotnet format --verify-no-changes` → clean
- [ ] `npm run build` → green
- [ ] `npm run lint` → clean
- [ ] Dev server: tile appears on dashboard, shows count `0` with red `AlertTriangle` and red count text
- [ ] Click the tile → browser navigates to `/hangfire/jobs/failed`
