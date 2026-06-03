# DataQuality Dashboard Tiles Drill-Down Route Unification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hard-coded frontend URLs (`/data-quality`, `/automation/data-quality`) inside the two DataQuality dashboard tiles with a semantic `routeKey` contract, and resolve the actual React-router path on the frontend so the backend no longer encodes any knowledge of the SPA route layout.

**Architecture:** Extract a shared `DashboardTileDrillDown` C# DTO under `Anela.Heblo.Application.Features.Dashboard.Contracts`. Both DataQuality tiles embed this DTO inside their anonymous tile payload with `RouteKey = "dataQuality"`. The frontend's existing `drillDownRoutes.ts` resolver is extended with a `dataQuality` entry pointing at `/automation/data-quality`. The two React tile components route through `resolveDrillDown(...)` instead of reading a raw `href`. A backend xUnit test per tile pins the wire shape; a CI grep guard enforces that backend tile files cannot regress to embedding frontend paths.

**Tech Stack:** .NET 8, ASP.NET Core, MediatR (existing tile framework `Anela.Heblo.Xcc.Services.Dashboard`), xUnit + Moq + FluentAssertions; React 18, TypeScript, React Router v6, Jest + Testing Library.

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — shared DTO (class, not record, per CLAUDE.md OpenAPI rule).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` — xUnit tests pinning JSON shape across `success` / `no_data` / `error` branches.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — xUnit tests pinning JSON shape across all branches plus `Running`/`Failed` statuses and tile metadata.
- `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx` — Jest+RTL tests covering navigate + unknown key.
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` — Jest+RTL tests covering all UI branches + navigate + unknown key.
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx` — Jest tests for the resolver (registry shape, both strategies, unknown-key warn, disabled, undefined).

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — replace inline `drillDown = new { href = ... }` with `new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }` in all three return paths; introduce `private const string DrillDownRouteKey = "dataQuality";`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — same migration across `no_data`, all status branches in the success path, and the `catch`.
- `frontend/src/components/dashboard/drillDownRoutes.ts` — extend `DashboardDrillDownRouteKey` union with `'dataQuality'`, add `dataQuality: { type: 'react-router', path: '/automation/data-quality' }` to `DASHBOARD_DRILLDOWN_ROUTES`.
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — drop hand-rolled `href` plumbing; call `resolveDrillDown(data.drillDown)`, navigate via `useNavigate()` for react-router strategy, `window.open` for external (kept for parity even though this tile is react-router only).
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — same migration; preserve `data-testid="dqt-yesterday-tile"` already used by other tests/E2E.

**Not touched in this PR (out of scope per spec):**
- `frontend/src/components/dashboard/tiles/TileContent.tsx` — the `tileId`-keyed dispatch for `Catalog`/`Logistics`/`Manufacture`/`Analytics`/`Purchase` stays as-is.
- `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` — already on the `routeKey` pattern; left untouched.
- `frontend/src/App.tsx` — route `/automation/data-quality` already mounted; no router change.

---

## Pre-flight checks

These are the prerequisites the arch review verified are already true. Re-verify in your workspace before starting; if any fails, stop and ask.

- [ ] **Step 1: Confirm `/automation/data-quality` is the live route.**

  Run:
  ```bash
  grep -n "automation/data-quality" frontend/src/App.tsx
  ```
  Expected: a line like `<Route path="/automation/data-quality" element={<DataQualityPage />} />` (location may vary; ~line 474 at time of arch review).

- [ ] **Step 2: Confirm the resolver already exists with the `hangfireFailedJobs` entry only.**

  Run:
  ```bash
  cat frontend/src/components/dashboard/drillDownRoutes.ts
  ```
  Expected: file exists, exports `resolveDrillDown`, contains `hangfireFailedJobs` mapping. (`dataQuality` is what this plan adds.) If the file is missing, fall back to creating it in Task 4 with the full contents shown there.

- [ ] **Step 3: Confirm `IDqtRunRepository` and `DqtRun` shape is unchanged.**

  Run:
  ```bash
  grep -n "GetLatestByTestTypeAsync\|GetLatestByTestTypeAndCoveredDateAsync" \
    backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs
  ```
  Expected: both methods present. The tiles call exactly these and no others.

- [ ] **Step 4: Confirm the working tree is clean and tests are green on `main`.**

  Run:
  ```bash
  git status -uno
  dotnet build backend/Anela.Heblo.sln
  ```
  Expected: clean working tree, build succeeds. If not, resolve before starting.

No commit at this stage — these are checks, not changes.

---

## Task 1: Create the shared `DashboardTileDrillDown` C# DTO

Introduce the contract that the rest of the plan depends on. The DTO lives under `Application/Features/Dashboard/Contracts/` (Dashboard module, not DataQuality) because it is a Dashboard concept consumed by every tile, not a DataQuality concept. `class` (not `record`) is mandatory per CLAUDE.md — OpenAPI-generated TS clients mishandle record constructors. `[JsonPropertyName]` attributes enforce camelCase on the wire while keeping PascalCase in C#.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`

- [ ] **Step 1: Verify the target directory exists**

  Run:
  ```bash
  ls backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/
  ```
  Expected: directory exists (it already contains other Dashboard DTOs like `DashboardTileDto.cs`). If it does not exist, create it:
  ```bash
  mkdir -p backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts
  ```

- [ ] **Step 2: Write the DTO file**

  Create `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` with exactly:
  ```csharp
  using System.Text.Json.Serialization;

  namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

  // Plain class (not a record) — DTO serialization rule from CLAUDE.md: OpenAPI client
  // generators mishandle record parameter order. Tile payloads use this DTO embedded in
  // an anonymous projection because LoadDataAsync returns Task<object>.
  public class DashboardTileDrillDown
  {
      [JsonPropertyName("routeKey")]
      public string RouteKey { get; set; } = string.Empty;

      [JsonPropertyName("enabled")]
      public bool Enabled { get; set; }

      [JsonPropertyName("parameters")]
      public IReadOnlyDictionary<string, string>? Parameters { get; set; }
  }
  ```

- [ ] **Step 3: Build the Application project**

  Run:
  ```bash
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```
  Expected: build succeeds with zero new warnings.

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs
  git commit -m "feat: add shared DashboardTileDrillDown DTO with routeKey contract"
  ```

---

## Task 2: Write failing xUnit tests for `DataQualityStatusTile`

TDD — the test names and shape pin FR-1, FR-2, and the arch-review amendment "Add a backend payload test" (assert `routeKey == "dataQuality"` and `enabled == true` for every status branch). FluentAssertions is already a project dependency (used by `DataQualityHandlerTests` etc.) so the assertion style matches surrounding tests. The tests serialise the anonymous return value with `System.Text.Json` exactly like the production pipeline does — this catches the case where the DTO would otherwise emit `RouteKey` (PascalCase) instead of `routeKey`.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs`

- [ ] **Step 1: Confirm the test directory exists or create it**

  Run:
  ```bash
  ls backend/test/Anela.Heblo.Tests/Features/DataQuality/ 2>/dev/null || \
    mkdir -p backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles
  mkdir -p backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles
  ```

- [ ] **Step 2: Write the failing test file**

  Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs` with:
  ```csharp
  using System.Text.Json;
  using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
  using Anela.Heblo.Domain.Features.DataQuality;
  using FluentAssertions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

  public class DataQualityStatusTileTests
  {
      private readonly Mock<IDqtRunRepository> _repositoryMock = new();
      private readonly DataQualityStatusTile _tile;

      public DataQualityStatusTileTests()
      {
          _tile = new DataQualityStatusTile(_repositoryMock.Object);
      }

      [Fact]
      public async Task LoadDataAsync_NoRun_ReturnsNoDataWithRouteKey()
      {
          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAsync(
                  DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
              .ReturnsAsync((DqtRun?)null);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
          doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);

          var drillDown = doc.RootElement.GetProperty("drillDown");
          drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
          drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
          drillDown.TryGetProperty("href", out _).Should().BeFalse();
          drillDown.TryGetProperty("url", out _).Should().BeFalse();
      }

      [Fact]
      public async Task LoadDataAsync_RunWithoutMismatches_ReturnsSuccessWithRouteKey()
      {
          var run = CreateCompletedRun(totalChecked: 50, totalMismatches: 0);

          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAsync(
                  DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
              .ReturnsAsync(run);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("success");
          doc.RootElement.GetProperty("drillDown").GetProperty("routeKey").GetString().Should().Be("dataQuality");
      }

      [Fact]
      public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorWithRouteKey()
      {
          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAsync(
                  DqtTestType.IssuedInvoiceComparison, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("db down"));

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("error");
          var drillDown = doc.RootElement.GetProperty("drillDown");
          drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
          drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
      }

      private static DqtRun CreateCompletedRun(int totalChecked, int totalMismatches)
      {
          var date = new DateOnly(2026, 5, 5);
          var run = DqtRun.Start(
              DqtTestType.IssuedInvoiceComparison,
              date,
              date,
              DqtTriggerType.Scheduled);
          run.Complete(totalChecked, totalMismatches);
          return run;
      }

      private static JsonDocument ToJsonDoc(object payload) =>
          JsonDocument.Parse(JsonSerializer.Serialize(payload));
  }
  ```

- [ ] **Step 3: Run the tests and verify they FAIL**

  Run:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~DataQualityStatusTileTests" --nologo
  ```
  Expected: each of the three tests fails — either the `drillDown` property is missing the `routeKey` field (the production tile still emits `href`) or the property names differ. **Do not proceed until tests fail for the right reason.** If they pass already, the production tile was already migrated; skip to Task 4.

- [ ] **Step 4: Do not commit yet** — the implementation in Task 3 makes them pass, and the test+impl land together.

---

## Task 3: Migrate `DataQualityStatusTile` to use the new DTO

Replace the inline anonymous `drillDown = new { href = "/data-quality" }` (or whatever it currently is — the spec brief flags two distinct hard-coded values across the two tiles) with the new `DashboardTileDrillDown { RouteKey = "dataQuality", Enabled = true }`. Use a single `private const string DrillDownRouteKey = "dataQuality";` referenced from all three return paths (FR-2 acceptance criterion).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs`

- [ ] **Step 1: Read the current file to find the three return sites**

  Run:
  ```bash
  cat backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs
  ```
  You should see three `return new { ... drillDown = ... }` blocks — `no_data`, the success/warning path, and the `catch`.

- [ ] **Step 2: Replace the entire file with the migrated version**

  Overwrite `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Dashboard.Contracts;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Anela.Heblo.Xcc.Services.Dashboard;

  namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

  [TileId("dataqualitystatus")]
  public class DataQualityStatusTile : ITile
  {
      private const string DrillDownRouteKey = "dataQuality";

      private readonly IDqtRunRepository _repository;

      public string Title => "Kvalita dat";
      public string Description => "Stav posledního DQT testu faktur";
      public TileSize Size => TileSize.Small;
      public TileCategory Category => TileCategory.DataQuality;
      public bool DefaultEnabled => true;
      public bool AutoShow => false;
      public Type ComponentType => typeof(object);
      public string[] RequiredPermissions => Array.Empty<string>();

      public DataQualityStatusTile(IDqtRunRepository repository)
      {
          _repository = repository;
      }

      public async Task<object> LoadDataAsync(Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
      {
          try
          {
              var run = await _repository.GetLatestByTestTypeAsync(DqtTestType.IssuedInvoiceComparison, cancellationToken);

              if (run is null)
              {
                  return new
                  {
                      status = "no_data",
                      data = (object?)null,
                      drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                  };
              }

              var statusStr = run.Status == DqtRunStatus.Failed
                  ? "error"
                  : run.TotalMismatches > 0
                      ? "warning"
                      : "success";

              return new
              {
                  status = statusStr,
                  data = new
                  {
                      runId = run.Id,
                      runStatus = run.Status.ToString(),
                      dateFrom = run.DateFrom,
                      dateTo = run.DateTo,
                      totalChecked = run.TotalChecked,
                      totalMismatches = run.TotalMismatches
                  },
                  drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
              };
          }
          catch
          {
              return new
              {
                  status = "error",
                  data = (object?)null,
                  drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
              };
          }
      }
  }
  ```

  Notes that matter:
  - Single `DrillDownRouteKey` constant referenced in all three return paths (FR-2 grep-clean criterion).
  - No literal beginning with `/` anywhere in the file (FR-2).
  - The `catch` block keeps the existing "swallow silently" behaviour. The arch review flagged this as a pre-existing concern but explicitly out of scope for this PR — **do not** add a logger here. Track separately.

- [ ] **Step 3: Verify the file is grep-clean of frontend paths**

  Run:
  ```bash
  grep -nE '/data-quality|/automation/' \
    backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs
  ```
  Expected: zero matches.

- [ ] **Step 4: Run the new tests — they must PASS**

  Run:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~DataQualityStatusTileTests" --nologo
  ```
  Expected: all three tests pass.

- [ ] **Step 5: Commit test + implementation together**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs \
          backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DataQualityStatusTileTests.cs
  git commit -m "refactor: migrate DataQualityStatusTile drillDown to routeKey contract"
  ```

---

## Task 4: Write failing xUnit tests for `DqtYesterdayStatusTile`

The yesterday tile has more branches than the small one — `no_data`, `Completed` with mismatches (`warning`), `Completed` with zero mismatches (`success`), `Running` (`warning`), `Failed` (`error`), and `catch`. The plan covers each so a future refactor of the status switch cannot silently drop a branch. A `TimeProvider` mock pins "yesterday" to `2026-05-05` so the repo lookup arg is deterministic. `NullLogger<DqtYesterdayStatusTile>.Instance` keeps the test focused on behaviour, not logging output. The final `TileMetadata_MatchesSpec` fact protects the literal strings the user sees in the dashboard catalogue.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs`

- [ ] **Step 1: Write the failing test file**

  Create `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` with:
  ```csharp
  using System.Text.Json;
  using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
  using Anela.Heblo.Domain.Features.DataQuality;
  using FluentAssertions;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.DataQuality.DashboardTiles;

  public class DqtYesterdayStatusTileTests
  {
      // Pinned "today" = 2026-05-06 10:00 UTC → yesterday = 2026-05-05
      private static readonly DateTimeOffset FixedNow = new(2026, 5, 6, 10, 0, 0, TimeSpan.Zero);
      private static readonly DateOnly Yesterday = new(2026, 5, 5);

      private readonly Mock<IDqtRunRepository> _repositoryMock = new();
      private readonly Mock<TimeProvider> _timeProviderMock = new();
      private readonly DqtYesterdayStatusTile _tile;

      public DqtYesterdayStatusTileTests()
      {
          _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

          _tile = new DqtYesterdayStatusTile(
              _repositoryMock.Object,
              _timeProviderMock.Object,
              NullLogger<DqtYesterdayStatusTile>.Instance);
      }

      [Fact]
      public async Task LoadDataAsync_NoRunCoveringYesterday_ReturnsNoData()
      {
          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ReturnsAsync((DqtRun?)null);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("no_data");
          doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
          var drillDown = doc.RootElement.GetProperty("drillDown");
          drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
          drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
          drillDown.TryGetProperty("href", out _).Should().BeFalse();
          drillDown.TryGetProperty("url", out _).Should().BeFalse();
      }

      [Fact]
      public async Task LoadDataAsync_CompletedWithZeroMismatches_ReturnsSuccess()
      {
          var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 0);

          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ReturnsAsync(run);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("success");
          var data = doc.RootElement.GetProperty("data");
          data.GetProperty("runStatus").GetString().Should().Be("Completed");
          data.GetProperty("totalChecked").GetInt32().Should().Be(123);
          data.GetProperty("totalMismatches").GetInt32().Should().Be(0);
      }

      [Fact]
      public async Task LoadDataAsync_CompletedWithMismatches_ReturnsWarning()
      {
          var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Completed, totalChecked: 123, totalMismatches: 4);

          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ReturnsAsync(run);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
          doc.RootElement.GetProperty("data").GetProperty("totalMismatches").GetInt32().Should().Be(4);
          doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Completed");
      }

      [Fact]
      public async Task LoadDataAsync_RunningRun_ReturnsWarningWithRunningStatus()
      {
          var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Running, totalChecked: 0, totalMismatches: 0);

          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ReturnsAsync(run);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("warning");
          doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Running");
      }

      [Fact]
      public async Task LoadDataAsync_FailedRun_ReturnsError()
      {
          var run = CreateRun(yesterday: Yesterday, status: DqtRunStatus.Failed, totalChecked: 0, totalMismatches: 0);

          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ReturnsAsync(run);

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("error");
          doc.RootElement.GetProperty("data").GetProperty("runStatus").GetString().Should().Be("Failed");
      }

      [Fact]
      public async Task LoadDataAsync_RepositoryThrows_ReturnsErrorAndDoesNotPropagate()
      {
          _repositoryMock
              .Setup(r => r.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison, Yesterday, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("db down"));

          var result = await _tile.LoadDataAsync();

          var doc = ToJsonDoc(result);
          doc.RootElement.GetProperty("status").GetString().Should().Be("error");
          doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
          var drillDown = doc.RootElement.GetProperty("drillDown");
          drillDown.GetProperty("routeKey").GetString().Should().Be("dataQuality");
          drillDown.GetProperty("enabled").GetBoolean().Should().BeTrue();
          drillDown.TryGetProperty("href", out _).Should().BeFalse();
          drillDown.TryGetProperty("url", out _).Should().BeFalse();
      }

      [Fact]
      public void TileMetadata_MatchesSpec()
      {
          _tile.Title.Should().Be("DQT včera");
          _tile.Description.Should().Be("Stav včerejšího DQT testu faktur");
          _tile.Size.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileSize.Medium);
          _tile.Category.Should().Be(Anela.Heblo.Xcc.Services.Dashboard.TileCategory.DataQuality);
          _tile.DefaultEnabled.Should().BeTrue();
          _tile.AutoShow.Should().BeFalse();
          _tile.RequiredPermissions.Should().BeEmpty();
      }

      private static DqtRun CreateRun(DateOnly yesterday, DqtRunStatus status, int totalChecked, int totalMismatches)
      {
          var run = DqtRun.Start(
              DqtTestType.IssuedInvoiceComparison,
              yesterday,
              yesterday,
              DqtTriggerType.Scheduled);

          if (status == DqtRunStatus.Completed)
          {
              run.Complete(totalChecked, totalMismatches);
          }
          else if (status == DqtRunStatus.Failed)
          {
              run.Fail("simulated failure");
          }
          // Running: leave as-is (DqtRun.Start sets Status = Running).
          return run;
      }

      private static JsonDocument ToJsonDoc(object payload) =>
          JsonDocument.Parse(JsonSerializer.Serialize(payload));
  }
  ```

- [ ] **Step 2: Run the tests and verify they FAIL**

  Run:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~DqtYesterdayStatusTileTests" --nologo
  ```
  Expected: tests fail because the production tile still emits the old shape (e.g. `href` instead of `routeKey`).

- [ ] **Step 3: Do not commit yet** — Task 5 makes them pass.

---

## Task 5: Migrate `DqtYesterdayStatusTile` to use the new DTO

Same migration as Task 3 — replace inline `href` with `DashboardTileDrillDown` in all four return paths (`no_data`, success path via `switch`, `catch`). Keep the existing logger usage in the `catch` (this tile already logs — the small tile is a separate pre-existing gap, out of scope here).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`

- [ ] **Step 1: Overwrite the file with the migrated version**

  Replace `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` with:
  ```csharp
  using Anela.Heblo.Application.Features.Dashboard.Contracts;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Anela.Heblo.Xcc.Services.Dashboard;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.DataQuality.DashboardTiles;

  [TileId("dqtyesterdaystatus")]
  public class DqtYesterdayStatusTile : ITile
  {
      private const string DrillDownRouteKey = "dataQuality";

      private readonly IDqtRunRepository _repository;
      private readonly TimeProvider _timeProvider;
      private readonly ILogger<DqtYesterdayStatusTile> _logger;

      public string Title => "DQT včera";
      public string Description => "Stav včerejšího DQT testu faktur";
      public TileSize Size => TileSize.Medium;
      public TileCategory Category => TileCategory.DataQuality;
      public bool DefaultEnabled => true;
      public bool AutoShow => false;
      public Type ComponentType => typeof(object);
      public string[] RequiredPermissions => Array.Empty<string>();

      public DqtYesterdayStatusTile(
          IDqtRunRepository repository,
          TimeProvider timeProvider,
          ILogger<DqtYesterdayStatusTile> logger)
      {
          _repository = repository;
          _timeProvider = timeProvider;
          _logger = logger;
      }

      public async Task<object> LoadDataAsync(
          Dictionary<string, string>? parameters = null,
          CancellationToken cancellationToken = default)
      {
          var yesterday = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1);

          try
          {
              var run = await _repository.GetLatestByTestTypeAndCoveredDateAsync(
                  DqtTestType.IssuedInvoiceComparison,
                  yesterday,
                  cancellationToken);

              if (run is null)
              {
                  return new
                  {
                      status = "no_data",
                      data = (object?)null,
                      drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
                  };
              }

              var statusStr = run.Status switch
              {
                  DqtRunStatus.Failed => "error",
                  DqtRunStatus.Running => "warning",
                  DqtRunStatus.Completed when run.TotalMismatches > 0 => "warning",
                  DqtRunStatus.Completed => "success",
                  _ => "error"
              };

              return new
              {
                  status = statusStr,
                  data = new
                  {
                      runId = run.Id,
                      runStatus = run.Status.ToString(),
                      dateFrom = run.DateFrom,
                      dateTo = run.DateTo,
                      totalChecked = run.TotalChecked,
                      totalMismatches = run.TotalMismatches
                  },
                  drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
              };
          }
          catch (Exception ex)
          {
              _logger.LogError(
                  ex,
                  "Failed to load yesterday DQT status for {TestType} on {TargetDate}",
                  DqtTestType.IssuedInvoiceComparison,
                  yesterday);

              return new
              {
                  status = "error",
                  data = (object?)null,
                  drillDown = new DashboardTileDrillDown { RouteKey = DrillDownRouteKey, Enabled = true }
              };
          }
      }
  }
  ```

- [ ] **Step 2: Run the tests — they must PASS**

  Run:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~DqtYesterdayStatusTileTests" --nologo
  ```
  Expected: all seven tests pass.

- [ ] **Step 3: Run the full backend test suite for both tiles together**

  Run:
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~DashboardTiles" --nologo
  ```
  Expected: 10 tests pass (3 from Task 3 + 7 from Task 5).

- [ ] **Step 4: Confirm both backend tile files are grep-clean**

  Run:
  ```bash
  grep -RnE '/data-quality|/automation/' \
    backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/
  ```
  Expected: zero matches (NFR-3 acceptance criterion).

- [ ] **Step 5: Commit test + implementation together**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs \
          backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs
  git commit -m "refactor: migrate DqtYesterdayStatusTile drillDown to routeKey contract"
  ```

---

## Task 6: Extend the frontend resolver registry

Add `dataQuality` to the existing closed union and registry. The registry pattern is already proven by `hangfireFailedJobs` (set up in a previous PR for `FailedJobsTile`). The decision matrix from the arch review applies: the closed `DashboardDrillDownRouteKey` union plus the `Record<DashboardDrillDownRouteKey, DrillDownTarget>` map is what gives the compile-time guarantee that the registry can't miss a key; unknown keys at the consumer site degrade gracefully via `console.warn` + `null`. Per the arch-review amendment, do **not** rephrase the resolver to throw on unknowns.

If `drillDownRoutes.ts` does not exist in your branch, replace the file body entirely with the contents below. Otherwise, the only edits are (a) extend the union and (b) add the `dataQuality` row to `DASHBOARD_DRILLDOWN_ROUTES`.

**Files:**
- Modify (or create): `frontend/src/components/dashboard/drillDownRoutes.ts`
- Create: `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx`

- [ ] **Step 1: Read the current resolver**

  Run:
  ```bash
  cat frontend/src/components/dashboard/drillDownRoutes.ts
  ```

- [ ] **Step 2: Replace the file body with the final version**

  Overwrite `frontend/src/components/dashboard/drillDownRoutes.ts` with:
  ```ts
  import { getConfig } from '../../config/runtimeConfig';

  export type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

  export type DrillDownTarget =
    | { type: 'react-router'; path: string }
    | { type: 'external'; path: string };

  export interface DashboardTileDrillDown {
    routeKey: string;
    enabled: boolean;
    parameters?: Record<string, string>;
  }

  export interface DrillDownResolution {
    url: string;
    strategy: DrillDownTarget['type'];
  }

  // Closed set: adding a backend route key without a frontend entry is a build error
  // at the consuming tile component (the union type narrows away unknowns).
  export const DASHBOARD_DRILLDOWN_ROUTES: Record<DashboardDrillDownRouteKey, DrillDownTarget> = {
    dataQuality: { type: 'react-router', path: '/automation/data-quality' },
    hangfireFailedJobs: { type: 'external', path: '/hangfire/jobs/failed' },
  };

  const isKnownRouteKey = (key: string): key is DashboardDrillDownRouteKey =>
    Object.prototype.hasOwnProperty.call(DASHBOARD_DRILLDOWN_ROUTES, key);

  export function resolveDrillDown(
    drillDown: DashboardTileDrillDown | undefined,
  ): DrillDownResolution | null {
    if (!drillDown || !drillDown.enabled) {
      return null;
    }

    if (!isKnownRouteKey(drillDown.routeKey)) {
      // Backend deployed ahead of frontend: leave the tile visible but non-interactive.
      console.warn(`[dashboard] Unknown drill-down route key: ${drillDown.routeKey}`);
      return null;
    }

    const target = DASHBOARD_DRILLDOWN_ROUTES[drillDown.routeKey];

    if (target.type === 'external') {
      const { apiUrl } = getConfig();
      return { url: `${apiUrl}${target.path}`, strategy: 'external' };
    }

    return { url: target.path, strategy: 'react-router' };
  }
  ```

  Notes:
  - The `DashboardTileDrillDown` interface is kept narrow (`routeKey: string`, not the closed union) because OpenAPI-generated TS would always emit `string` here anyway. The closed-set guarantee comes from the registry, not the wire type.
  - `parameters` is exposed on the interface but unused; the spec leaves it for future parameterised destinations (Out of Scope).
  - The `console.warn` is intentionally not deduplicated. The arch review marked the spam risk as low; only revisit if it becomes noisy in practice.

- [ ] **Step 3: Write the resolver tests**

  Create `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx` with:
  ```tsx
  import {
    DASHBOARD_DRILLDOWN_ROUTES,
    resolveDrillDown,
  } from '../drillDownRoutes';

  jest.mock('../../../config/runtimeConfig', () => ({
    getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
  }));

  describe('drillDownRoutes', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

    beforeEach(() => {
      warnSpy.mockClear();
    });

    afterAll(() => {
      warnSpy.mockRestore();
    });

    it('registry contains the known route keys', () => {
      expect(DASHBOARD_DRILLDOWN_ROUTES.dataQuality).toEqual({
        type: 'react-router',
        path: '/automation/data-quality',
      });
      expect(DASHBOARD_DRILLDOWN_ROUTES.hangfireFailedJobs).toEqual({
        type: 'external',
        path: '/hangfire/jobs/failed',
      });
    });

    it('resolves a react-router key to the registered path with strategy "react-router"', () => {
      const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: true });

      expect(result).toEqual({
        url: '/automation/data-quality',
        strategy: 'react-router',
      });
      expect(warnSpy).not.toHaveBeenCalled();
    });

    it('resolves an external key by prepending apiUrl and strategy "external"', () => {
      const result = resolveDrillDown({ routeKey: 'hangfireFailedJobs', enabled: true });

      expect(result).toEqual({
        url: 'http://localhost:5001/hangfire/jobs/failed',
        strategy: 'external',
      });
      expect(warnSpy).not.toHaveBeenCalled();
    });

    it('returns null and warns for an unknown route key', () => {
      const result = resolveDrillDown({ routeKey: 'someUnknownKey', enabled: true });

      expect(result).toBeNull();
      expect(warnSpy).toHaveBeenCalledWith(
        expect.stringContaining('someUnknownKey'),
      );
    });

    it('returns null when the drill-down is disabled', () => {
      const result = resolveDrillDown({ routeKey: 'dataQuality', enabled: false });

      expect(result).toBeNull();
      expect(warnSpy).not.toHaveBeenCalled();
    });

    it('returns null when the drill-down is undefined', () => {
      const result = resolveDrillDown(undefined);

      expect(result).toBeNull();
      expect(warnSpy).not.toHaveBeenCalled();
    });
  });
  ```

- [ ] **Step 4: Run the resolver tests and verify they PASS**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false \
    components/dashboard/__tests__/drillDownRoutes.test.tsx
  ```
  Expected: 6 tests pass.

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/components/dashboard/drillDownRoutes.ts \
          frontend/src/components/dashboard/__tests__/drillDownRoutes.test.tsx
  git commit -m "feat: add dataQuality entry to dashboard drillDown route registry"
  ```

---

## Task 7: Write failing tests for `DataQualityTile.tsx`

The tile's existing tests (if any) assume an `href` field on the data prop. The new tests cover the three behaviours the spec mandates: navigate via SPA router on click in any clickable state (FR-4), no navigation when the key is unknown (FR-5), and `console.warn` is emitted with the unknown key in the message (FR-5).

`react-router-dom` is mocked so `useNavigate()` returns a Jest spy. `runtimeConfig` is mocked because the resolver imports it transitively — the `dataQuality` strategy is `react-router` so `apiUrl` is never actually read, but the mock prevents `getConfig()` from blowing up if env detection fails in a test runner.

**Files:**
- Create: `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx`

- [ ] **Step 1: Confirm or create the tile test directory**

  Run:
  ```bash
  mkdir -p frontend/src/components/dashboard/tiles/__tests__
  ```

- [ ] **Step 2: Write the failing test file**

  Create `frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx` with:
  ```tsx
  import React from 'react';
  import { render, screen, fireEvent } from '@testing-library/react';
  import { BrowserRouter } from 'react-router-dom';
  import { DataQualityTile } from '../DataQualityTile';

  const mockNavigate = jest.fn();
  jest.mock('react-router-dom', () => {
    const actual = jest.requireActual('react-router-dom');
    return { ...actual, useNavigate: () => mockNavigate };
  });

  jest.mock('../../../../config/runtimeConfig', () => ({
    getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
  }));

  const drillDown = { routeKey: 'dataQuality', enabled: true };

  const renderTile = (data: any) =>
    render(
      <BrowserRouter>
        <DataQualityTile data={data} />
      </BrowserRouter>,
    );

  beforeEach(() => {
    mockNavigate.mockReset();
  });

  describe('DataQualityTile', () => {
    it('navigates to /automation/data-quality on click in the no_data state', () => {
      renderTile({ status: 'no_data', drillDown });

      fireEvent.click(screen.getByText('Žádná data'));
      expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
    });

    it('navigates on click in the success state', () => {
      renderTile({
        status: 'success',
        data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
        drillDown,
      });

      fireEvent.click(screen.getByText('vše OK'));
      expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
    });

    it('does not navigate when the route key is unknown', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      renderTile({
        status: 'success',
        data: { mismatchCount: 0, totalChecked: 100, dateFrom: '2026-05-05', dateTo: '2026-05-05' },
        drillDown: { routeKey: 'somethingNew', enabled: true },
      });

      fireEvent.click(screen.getByText('vše OK'));
      expect(mockNavigate).not.toHaveBeenCalled();
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
      warnSpy.mockRestore();
    });
  });
  ```

- [ ] **Step 3: Run the tests and verify they FAIL**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false \
    components/dashboard/tiles/__tests__/DataQualityTile.test.tsx
  ```
  Expected: tests fail because the production component still reads `data.drillDown.href` (or similar) and never calls `navigate`.

- [ ] **Step 4: Do not commit yet** — Task 8 makes them pass.

---

## Task 8: Migrate `DataQualityTile.tsx` to use `resolveDrillDown`

Replace the inline `href` read with `resolveDrillDown(data.drillDown)`. Use `useNavigate()` for the `react-router` strategy and `window.open` for `external`. The `external` branch is dead code for the current `dataQuality` key, but keep it for parity with the `FailedJobsTile` pattern — it's two lines and keeps the consumer pattern uniform across tiles.

Render the no-affordance state when `resolution` is `null`. Specifically: the error state never accepts clicks (matches the pre-existing UX); the `no_data` and primary states still render exactly as before, but `handleClick` becomes a no-op when there is no resolution. **Do not** suppress the cursor/hover styling on a missing resolution — keeping it visible would be misleading, but the existing tests assume the same hover affordance in all clickable states, so leave the styling rules alone and rely on `handleClick` being a no-op. This is the FR-5 "tile remains visible and loads its data, but does not crash or navigate" contract.

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/DataQualityTile.tsx`

- [ ] **Step 1: Overwrite the file with the migrated version**

  Replace `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` with:
  ```tsx
  import React from 'react';
  import { useNavigate } from 'react-router-dom';
  import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
  import {
    DashboardTileDrillDown,
    resolveDrillDown,
  } from '../drillDownRoutes';

  interface DataQualityTileProps {
    data: {
      status?: string;
      data?: {
        mismatchCount?: number;
        totalChecked?: number;
        dateFrom?: string;
        dateTo?: string;
      };
      error?: string;
      drillDown?: DashboardTileDrillDown;
    };
  }

  export const DataQualityTile: React.FC<DataQualityTileProps> = ({ data }) => {
    const navigate = useNavigate();
    const resolution = resolveDrillDown(data.drillDown);

    const handleClick = () => {
      if (!resolution) {
        return;
      }
      if (resolution.strategy === 'react-router') {
        navigate(resolution.url);
      } else {
        window.open(resolution.url, '_blank');
      }
    };

    if (data.status === 'error') {
      return (
        <div className="h-full flex items-center justify-center text-center">
          <div>
            <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
            <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
          </div>
        </div>
      );
    }

    if (data.status === 'no_data') {
      return (
        <div
          className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
          onClick={handleClick}
          style={{ touchAction: 'manipulation' }}
        >
          <Clock className="h-10 w-10 text-gray-400 mb-2" />
          <p className="text-sm text-gray-500">Žádná data</p>
          <p className="text-xs text-gray-400 mt-1">Spusťte první kontrolu</p>
        </div>
      );
    }

    const mismatchCount = data.data?.mismatchCount ?? 0;
    const totalChecked = data.data?.totalChecked ?? 0;
    const dateFrom = data.data?.dateFrom;
    const dateTo = data.data?.dateTo;

    const hasMismatches = mismatchCount > 0;
    const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
    const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

    const dateRange =
      dateFrom && dateTo
        ? `${dateFrom} – ${dateTo}`
        : null;

    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
      >
        <div className={`mb-2 ${iconColor}`}>
          {hasMismatches ? (
            <AlertTriangle className="h-10 w-10" />
          ) : (
            <ShieldCheck className="h-10 w-10" />
          )}
        </div>
        <div className={`text-3xl font-bold mb-1 ${countColor}`}>
          {mismatchCount}
        </div>
        <div className="text-sm text-gray-500">
          {hasMismatches ? 'neshod' : 'vše OK'}
        </div>
        {totalChecked > 0 && (
          <div className="text-xs text-gray-400 mt-1">
            z {totalChecked} faktur
          </div>
        )}
        {dateRange && (
          <div className="text-xs text-gray-400 mt-1">
            {dateRange}
          </div>
        )}
      </div>
    );
  };
  ```

- [ ] **Step 2: Run the tests — they must PASS**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false \
    components/dashboard/tiles/__tests__/DataQualityTile.test.tsx
  ```
  Expected: all three tests pass.

- [ ] **Step 3: Confirm no literal `/` path strings remain in the tile**

  Run:
  ```bash
  grep -nE "['\"]/" frontend/src/components/dashboard/tiles/DataQualityTile.tsx || true
  ```
  Expected: zero matches (FR-4 acceptance criterion).

- [ ] **Step 4: Commit test + implementation together**

  ```bash
  git add frontend/src/components/dashboard/tiles/DataQualityTile.tsx \
          frontend/src/components/dashboard/tiles/__tests__/DataQualityTile.test.tsx
  git commit -m "refactor: route DataQualityTile through resolveDrillDown"
  ```

---

## Task 9: Write failing tests for `DqtYesterdayStatusTile.tsx`

This tile has more visual branches: `no_data`, `error` (no navigation), `warning + Running`, `warning + Completed with mismatches`, `success`, fallback "včera" label when `dateTo` is missing, navigate on click in clickable states, and the unknown-key no-op. The `data-testid="dqt-yesterday-tile"` is used by existing E2E tests — preserve it.

**Files:**
- Create: `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx`

- [ ] **Step 1: Write the failing test file**

  Create `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` with:
  ```tsx
  import React from 'react';
  import { render, screen, fireEvent } from '@testing-library/react';
  import { BrowserRouter } from 'react-router-dom';
  import { DqtYesterdayStatusTile } from '../DqtYesterdayStatusTile';

  const mockNavigate = jest.fn();
  jest.mock('react-router-dom', () => {
    const actual = jest.requireActual('react-router-dom');
    return { ...actual, useNavigate: () => mockNavigate };
  });

  jest.mock('../../../../config/runtimeConfig', () => ({
    getConfig: () => ({ apiUrl: 'http://localhost:5001' }),
  }));

  const drillDown = { routeKey: 'dataQuality', enabled: true };

  const renderTile = (data: any) =>
    render(
      <BrowserRouter>
        <DqtYesterdayStatusTile data={data} />
      </BrowserRouter>,
    );

  beforeEach(() => {
    mockNavigate.mockReset();
  });

  describe('DqtYesterdayStatusTile', () => {
    it('renders no_data state', () => {
      renderTile({ status: 'no_data', data: null, drillDown });

      expect(screen.getByText('Žádná data')).toBeInTheDocument();
      expect(screen.getByText('Včerejší test neproběhl')).toBeInTheDocument();
    });

    it('renders error state and does not navigate on click', () => {
      renderTile({ status: 'error', data: null, drillDown });

      expect(screen.getByText('Chyba při načítání dat')).toBeInTheDocument();
      expect(mockNavigate).not.toHaveBeenCalled();
    });

    it('renders warning state with Running runStatus', () => {
      renderTile({
        status: 'warning',
        data: {
          runId: 'r1',
          runStatus: 'Running',
          dateFrom: '2026-05-05',
          dateTo: '2026-05-05',
          totalChecked: 0,
          totalMismatches: 0,
        },
        drillDown,
      });

      expect(screen.getByText('probíhá')).toBeInTheDocument();
      expect(screen.getByText('05.05.2026')).toBeInTheDocument();
    });

    it('renders warning state with completed run and mismatches', () => {
      renderTile({
        status: 'warning',
        data: {
          runId: 'r2',
          runStatus: 'Completed',
          dateFrom: '2026-05-05',
          dateTo: '2026-05-05',
          totalChecked: 123,
          totalMismatches: 4,
        },
        drillDown,
      });

      expect(screen.getByText('4')).toBeInTheDocument();
      expect(screen.getByText('neshod')).toBeInTheDocument();
      expect(screen.getByText('z 123 faktur')).toBeInTheDocument();
      expect(screen.getByText('05.05.2026')).toBeInTheDocument();
    });

    it('renders success state', () => {
      renderTile({
        status: 'success',
        data: {
          runId: 'r3',
          runStatus: 'Completed',
          dateFrom: '2026-05-05',
          dateTo: '2026-05-05',
          totalChecked: 200,
          totalMismatches: 0,
        },
        drillDown,
      });

      expect(screen.getByText('0')).toBeInTheDocument();
      expect(screen.getByText('vše OK')).toBeInTheDocument();
      expect(screen.getByText('z 200 faktur')).toBeInTheDocument();
      expect(screen.getByText('05.05.2026')).toBeInTheDocument();
    });

    it('falls back to "včera" label when dateTo is missing', () => {
      renderTile({
        status: 'warning',
        data: {
          runId: 'r4',
          runStatus: 'Running',
          totalChecked: 0,
          totalMismatches: 0,
        },
        drillDown,
      });

      expect(screen.getByText('včera')).toBeInTheDocument();
    });

    it('navigates to /automation/data-quality when clicked (success state)', () => {
      renderTile({
        status: 'success',
        data: {
          runId: 'r5',
          runStatus: 'Completed',
          dateFrom: '2026-05-05',
          dateTo: '2026-05-05',
          totalChecked: 50,
          totalMismatches: 0,
        },
        drillDown,
      });

      const tile = screen.getByTestId('dqt-yesterday-tile');
      fireEvent.click(tile);
      expect(mockNavigate).toHaveBeenCalledWith('/automation/data-quality');
    });

    it('does not navigate when the route key is unknown', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      renderTile({
        status: 'success',
        data: {
          runId: 'r6',
          runStatus: 'Completed',
          dateFrom: '2026-05-05',
          dateTo: '2026-05-05',
          totalChecked: 50,
          totalMismatches: 0,
        },
        drillDown: { routeKey: 'somethingNew', enabled: true },
      });

      fireEvent.click(screen.getByTestId('dqt-yesterday-tile'));
      expect(mockNavigate).not.toHaveBeenCalled();
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('somethingNew'));
      warnSpy.mockRestore();
    });
  });
  ```

- [ ] **Step 2: Run the tests and verify they FAIL**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false \
    components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx
  ```
  Expected: most tests fail because the production component still uses the old `href` shape.

- [ ] **Step 3: Do not commit yet** — Task 10 makes them pass.

---

## Task 10: Migrate `DqtYesterdayStatusTile.tsx` to use `resolveDrillDown`

Same migration as Task 8. Preserve the `formatYesterdayLabel(iso)` helper that converts an ISO date string to `dd.MM.yyyy` Czech display format, the `data-testid="dqt-yesterday-tile"` attribute on every clickable variant, and the existing per-status visual branches (`error`, `no_data`, `warning + Running`, default).

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`

- [ ] **Step 1: Overwrite the file with the migrated version**

  Replace `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` with:
  ```tsx
  import React from 'react';
  import { useNavigate } from 'react-router-dom';
  import { ShieldCheck, AlertTriangle, XCircle, Clock } from 'lucide-react';
  import {
    DashboardTileDrillDown,
    resolveDrillDown,
  } from '../drillDownRoutes';

  interface DqtYesterdayStatusTileData {
    status?: 'success' | 'warning' | 'error' | 'no_data';
    data?: {
      runId?: string;
      runStatus?: 'Completed' | 'Failed' | 'Running';
      dateFrom?: string;
      dateTo?: string;
      totalChecked?: number;
      totalMismatches?: number;
    } | null;
    drillDown?: DashboardTileDrillDown;
  }

  interface DqtYesterdayStatusTileProps {
    data: DqtYesterdayStatusTileData;
  }

  const formatYesterdayLabel = (iso?: string): string => {
    if (!iso) return 'včera';
    const dateOnly = iso.slice(0, 10);
    const parts = dateOnly.split('-');
    if (parts.length !== 3) return 'včera';
    return `${parts[2]}.${parts[1]}.${parts[0]}`;
  };

  export const DqtYesterdayStatusTile: React.FC<DqtYesterdayStatusTileProps> = ({ data }) => {
    const navigate = useNavigate();
    const resolution = resolveDrillDown(data.drillDown);

    const handleClick = () => {
      if (!resolution) {
        return;
      }
      if (resolution.strategy === 'react-router') {
        navigate(resolution.url);
      } else {
        window.open(resolution.url, '_blank');
      }
    };

    if (data.status === 'error') {
      return (
        <div className="h-full flex items-center justify-center text-center">
          <div>
            <XCircle className="h-10 w-10 text-red-500 mx-auto mb-2" />
            <p className="text-red-600 text-sm">Chyba při načítání dat</p>
          </div>
        </div>
      );
    }

    if (data.status === 'no_data') {
      return (
        <div
          className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
          onClick={handleClick}
          style={{ touchAction: 'manipulation' }}
          data-testid="dqt-yesterday-tile"
        >
          <Clock className="h-10 w-10 text-gray-400 mb-2" />
          <p className="text-sm text-gray-500">Žádná data</p>
          <p className="text-xs text-gray-400 mt-1">Včerejší test neproběhl</p>
        </div>
      );
    }

    const runStatus = data.data?.runStatus;
    const dateLabel = formatYesterdayLabel(data.data?.dateTo);

    if (data.status === 'warning' && runStatus === 'Running') {
      return (
        <div
          className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
          onClick={handleClick}
          style={{ touchAction: 'manipulation' }}
          data-testid="dqt-yesterday-tile"
        >
          <Clock className="h-10 w-10 text-amber-500 mb-2" />
          <p className="text-sm text-amber-600">probíhá</p>
          <p className="text-xs text-gray-400 mt-1">{dateLabel}</p>
        </div>
      );
    }

    const totalMismatches = data.data?.totalMismatches ?? 0;
    const totalChecked = data.data?.totalChecked ?? 0;
    const hasMismatches = totalMismatches > 0;
    const iconColor = hasMismatches ? 'text-red-500' : 'text-green-500';
    const countColor = hasMismatches ? 'text-red-700' : 'text-green-700';

    return (
      <div
        className="flex flex-col items-center justify-center h-full leading-relaxed min-h-44 cursor-pointer hover:bg-gray-50 active:bg-gray-100 transition-colors duration-200 rounded-lg"
        onClick={handleClick}
        style={{ touchAction: 'manipulation' }}
        data-testid="dqt-yesterday-tile"
      >
        <div className={`mb-2 ${iconColor}`}>
          {hasMismatches ? (
            <AlertTriangle className="h-10 w-10" />
          ) : (
            <ShieldCheck className="h-10 w-10" />
          )}
        </div>
        <div className={`text-3xl font-bold mb-1 ${countColor}`}>{totalMismatches}</div>
        <div className="text-sm text-gray-500">{hasMismatches ? 'neshod' : 'vše OK'}</div>
        {totalChecked > 0 && (
          <div className="text-xs text-gray-400 mt-1">z {totalChecked} faktur</div>
        )}
        <div className="text-xs text-gray-400 mt-1">{dateLabel}</div>
      </div>
    );
  };
  ```

- [ ] **Step 2: Run the tile tests — they must PASS**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false \
    components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx
  ```
  Expected: 8 tests pass.

- [ ] **Step 3: Confirm no literal `/` path strings remain**

  Run:
  ```bash
  grep -nE "['\"]/" frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx || true
  ```
  Expected: zero matches.

- [ ] **Step 4: Commit test + implementation together**

  ```bash
  git add frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx \
          frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx
  git commit -m "refactor: route DqtYesterdayStatusTile through resolveDrillDown"
  ```

---

## Task 11: Full-suite validation, formatter, and grep guard

Sweep the whole change before declaring done. This covers the project's "Validation before completion" rule (`CLAUDE.md`): backend build + format, frontend build + lint, all touched tests green, and the arch-review maintainability lint that proves the rule has not regressed.

- [ ] **Step 1: Run the full backend solution build**

  Run:
  ```bash
  dotnet build backend/Anela.Heblo.sln
  ```
  Expected: build succeeds with zero new warnings.

- [ ] **Step 2: Run all backend tests**

  Run:
  ```bash
  dotnet test backend/Anela.Heblo.sln --nologo
  ```
  Expected: all tests pass. The DataQuality dashboard tile tests should appear and pass (10 new tests).

- [ ] **Step 3: Format C# sources**

  Run:
  ```bash
  dotnet format backend/Anela.Heblo.sln
  ```
  Expected: completes cleanly. If it makes edits, stage them and amend with `git add -p && git commit --amend --no-edit` only for trivial whitespace; if it makes substantive changes, treat them as a separate `chore: dotnet format` commit.

- [ ] **Step 4: Build the frontend bundle**

  Run:
  ```bash
  npm --prefix frontend run build
  ```
  Expected: build succeeds.

- [ ] **Step 5: Lint the frontend**

  Run:
  ```bash
  npm --prefix frontend run lint
  ```
  Expected: zero errors.

- [ ] **Step 6: Run all dashboard frontend tests**

  Run:
  ```bash
  npm --prefix frontend test -- --watchAll=false components/dashboard
  ```
  Expected: all dashboard-related Jest tests pass, including the three suites this PR adds (`drillDownRoutes`, `DataQualityTile`, `DqtYesterdayStatusTile`).

- [ ] **Step 7: Enforce the grep-clean rule for backend tile files (NFR-3)**

  Run:
  ```bash
  grep -RnE '/data-quality|/automation/' \
    backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/
  ```
  Expected: zero matches. If anything appears, the rule has regressed — fix and re-run before merging.

  This grep is the maintainability lint called out by the arch-review amendment. To make it durable, add it to your local pre-commit / CI check (out of scope for this PR but recommended in the PR description).

- [ ] **Step 8: Manual smoke test (recommended, not required)**

  Start the BE and FE locally per `docs/development/setup.md` and visit the dashboard. Both DataQuality tiles ("Kvalita dat" small and "DQT včera" medium) should render with status content. Clicking either tile must SPA-navigate (no full reload) to `/automation/data-quality`. The Hangfire "Failed Jobs" tile must still `window.open` an external `apiUrl + /hangfire/jobs/failed` URL.

- [ ] **Step 9: Final commit if `dotnet format` produced edits**

  Only if Step 3 produced edits:
  ```bash
  git status
  git add -A
  git commit -m "chore: dotnet format after drillDown refactor"
  ```

---

## Self-review against the spec and arch review

This is a check the implementer should run mentally before opening the PR. Each item maps a spec requirement to the task that satisfies it.

| Spec / Amendment item | Covered by |
|---|---|
| FR-1: shared `DashboardTileDrillDown` DTO in correct namespace, JSON property names camelCase, no path/URL members | Task 1 |
| FR-2: both tiles emit `routeKey = "dataQuality"` in success/no_data/error branches; single per-tile constant; no `/`-prefixed literals | Tasks 3, 5; verified by grep in Step 4 of Task 5 and Step 7 of Task 11 |
| FR-3: `DashboardDrillDownRouteKey` closed union, `DASHBOARD_DRILLDOWN_ROUTES` map, `resolveDrillDown` → null for missing/disabled/unknown, `console.warn` on unknown, external prefixed with `apiUrl` | Task 6 |
| FR-4: tiles drive drill-down through `resolveDrillDown`; null → no nav; react-router strategy uses SPA router; external strategy opens external URL | Tasks 8, 10 |
| FR-5: unknown keys → null + `console.warn`, tile still renders | Tasks 6, 7, 9 |
| NFR-1 performance: O(1) in-memory lookup, no extra round-trip | Inherent to Task 6 design |
| NFR-2 security: closed allow-list, external anchored to `apiUrl`, raw user URLs never navigated | Inherent to Task 6 design |
| NFR-3 maintainability: backend tile files grep-clean for frontend paths; closed union enforces build error when extended without registry entry | Task 11 Step 7; Task 6 |
| NFR-4: single-deploy artefact makes coordinated cutover automatic — dual-emit removed per arch-review amendment 2 | Implicit in single-commit migration |
| Arch-review amendment 1 (NFR-3 wording) | Task 6 step 2 notes |
| Arch-review amendment 2 (drop dual-emit) | Single-PR hard cutover; no `href` legacy emitted anywhere |
| Arch-review amendment 3 (backend payload tests) | Tasks 2, 4 (one xUnit test per tile per branch) |
| Arch-review amendment 4 (maintainability grep guard) | Task 11 Step 7; called out in PR description as a future CI/pre-commit addition |

**Placeholder scan:** no TODO, no "TBD", no "similar to Task N" without showing the code, no "fill in details". Every step contains either the complete file body, the exact command, or a clear binary check.

**Type consistency check:** the C# constant `DrillDownRouteKey = "dataQuality"` matches the TS union member `'dataQuality'` matches the registry key `dataQuality` matches the route path `/automation/data-quality` — verified at three sites across Tasks 3, 5, and 6. The TS interface `DashboardTileDrillDown` field set (`routeKey`, `enabled`, `parameters`) matches the C# `[JsonPropertyName]` attributes in Task 1.
