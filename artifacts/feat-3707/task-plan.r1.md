# Task Plan: Unit tests for PackingStatsTile failure isolation

## Goal
Add a unit-test class that pins the three response shapes of `PackingStatsTile.LoadDataAsync` (happy path, Shoptet graceful-degradation, repository total-failure), closing a 0% coverage gap without touching production code.

## Architecture
One new sealed xUnit test class, `PackingStatsTileTests`, constructs the `PackingStatsTile` SUT directly with `Mock<IPackageRepository>`, `Mock<IPackingOrderClient>`, a hand-rolled `FakeTimeProvider : TimeProvider` (Prague +02:00, copied from `GetPackingDashboardHandlerTests`), and `NullLogger<PackingStatsTile>.Instance`. Because `LoadDataAsync` returns `Task<object>` wrapping an anonymous type, assertions serialize the result with `System.Text.Json` and read a `JsonDocument` (the `ToJsonDoc` helper from `FailedJobsTileTests`), which doubles as the true dashboard wire-contract check. These are characterization tests over already-correct production code, so they go green on the first run; the "verify" steps confirm that green and guard against future refactors.

### task: add-packingstatstile-unit-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs`

Notes grounded in source (verified, do not deviate):
- SUT ctor: `PackingStatsTile(IPackageRepository repo, IPackingOrderClient packingOrderClient, TimeProvider timeProvider, ILogger<PackingStatsTile> logger)` — namespace `Anela.Heblo.Application.Features.Packaging.DashboardTiles`.
- `IPackageRepository.GetPackedTodayByPackerAsync(DateTimeOffset, DateTimeOffset, CancellationToken)` → `Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>` (namespace `Anela.Heblo.Domain.Features.Packaging`).
- `IPackingOrderClient.GetOrdersBeingPackedCountAsync(CancellationToken)` / `GetOrdersBeingProcessedCountAsync(CancellationToken)` → `Task<int>` (namespace `Anela.Heblo.Application.Features.ShoptetOrders`).
- `PackerPackingSummary(Guid? PackedByUserId, string? PackedBy, int DistinctOrderCount)` — positional record; construct as `new(guid, "Alice", 4)`.
- Success anonymous shape keys (verbatim JSON): `status`, `data.{ordersBeingPackedCount, ordersBeingProcessedCount, ordersBeingPackedCountLastSync, totalOrdersPackedToday, packedByPacker[].{packerId, packerName, orderCount}}`, `metadata.{lastUpdated, source}`, `drillDown.{filters, enabled, tooltip}`.
- Error anonymous shape: `{ status = "error", error = "Nepodařilo se načíst data balení" }` — **`data` property is absent, not null**. Assert absence with `TryGetProperty("data", out _).Should().BeFalse()`. Do NOT copy `FailedJobsTileTests`' `data == JsonValueKind.Null` assertion — the tile omits the property.
- Null `PackedBy` maps to `packerName == "Neznámý"`; null `PackedByUserId` serializes as `JsonValueKind.Null`; a set `PackedByUserId` (`Guid?`) serializes as a JSON string.

- [ ] **Step 1: Create the folder and write the complete test file.**

The `Features/Packaging/DashboardTiles/` folder does not yet exist under the test project — the Write tool creates it. Write the file exactly as below:

```csharp
using System.Net.Http;
using System.Text.Json;
using Anela.Heblo.Application.Features.Packaging.DashboardTiles;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging.DashboardTiles;

public sealed class PackingStatsTileTests
{
    private static readonly DateTimeOffset PragueNow =
        new(2026, 6, 10, 14, 30, 0, TimeSpan.FromHours(2));

    // TimeProvider.GetLocalNow() is not virtual, so we subclass instead of using Moq.
    // Overriding GetUtcNow() + LocalTimeZone makes GetLocalNow() return PragueNow correctly.
    // Mirrors the FakeTimeProvider used by GetPackingDashboardHandlerTests in the sibling folder.
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _localNow;
        private readonly TimeZoneInfo _zone;

        public FakeTimeProvider(DateTimeOffset localNow)
        {
            _localNow = localNow;
            _zone = TimeZoneInfo.CreateCustomTimeZone(
                "FakeZone", localNow.Offset, "FakeZone", "FakeZone");
        }

        public override DateTimeOffset GetUtcNow() => _localNow.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => _zone;
    }

    private readonly Mock<IPackageRepository> _repo = new();
    private readonly Mock<IPackingOrderClient> _packingClient = new();

    private PackingStatsTile MakeSut() =>
        new(
            _repo.Object,
            _packingClient.Object,
            new FakeTimeProvider(PragueNow),
            NullLogger<PackingStatsTile>.Instance);

    private static JsonDocument ToJsonDoc(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload));

    // FR-1 (happy path) + FR-3 (null packer name -> "Neznámý"), folded per arch-review Decision 3.
    [Fact]
    public async Task LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers()
    {
        // Arrange
        var packerId = Guid.NewGuid();
        var byPacker = new List<PackerPackingSummary>
        {
            new(packerId, "Alice", 4),
            new(null, null, 1), // null name -> "Neznámý", null id -> JSON null
        };
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((6, byPacker));
        _packingClient
            .Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _packingClient
            .Setup(c => c.GetOrdersBeingProcessedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var sut = MakeSut();

        // Act
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");

        var data = root.GetProperty("data");
        data.GetProperty("ordersBeingPackedCount").GetInt32().Should().Be(4);
        data.GetProperty("ordersBeingProcessedCount").GetInt32().Should().Be(9);
        data.GetProperty("ordersBeingPackedCountLastSync").ValueKind.Should().Be(JsonValueKind.String);
        data.GetProperty("totalOrdersPackedToday").GetInt32().Should().Be(6);

        var packers = data.GetProperty("packedByPacker");
        packers.GetArrayLength().Should().Be(2);

        packers[0].GetProperty("packerId").GetString().Should().Be(packerId.ToString());
        packers[0].GetProperty("packerName").GetString().Should().Be("Alice");
        packers[0].GetProperty("orderCount").GetInt32().Should().Be(4);

        packers[1].GetProperty("packerId").ValueKind.Should().Be(JsonValueKind.Null);
        packers[1].GetProperty("packerName").GetString().Should().Be("Neznámý"); // FR-3
        packers[1].GetProperty("orderCount").GetInt32().Should().Be(1);
    }

    // FR-2: Shoptet failure is isolated — tile stays "success", only Shoptet-derived fields null.
    [Fact]
    public async Task LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated()
    {
        // Arrange
        var byPacker = new List<PackerPackingSummary> { new(Guid.NewGuid(), "Alice", 3) };
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((5, byPacker));
        _packingClient
            .Setup(c => c.GetOrdersBeingPackedCountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unreachable"));
        _packingClient
            .Setup(c => c.GetOrdersBeingProcessedCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var sut = MakeSut();

        // Act — must not throw (inner catch swallows the Shoptet exception)
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");

        var data = root.GetProperty("data");
        data.GetProperty("ordersBeingPackedCount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("ordersBeingProcessedCount").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("ordersBeingPackedCountLastSync").ValueKind.Should().Be(JsonValueKind.Null);

        // Packer data is untouched — proves failure isolation.
        data.GetProperty("totalOrdersPackedToday").GetInt32().Should().Be(5);
        var packers = data.GetProperty("packedByPacker");
        packers.GetArrayLength().Should().Be(1);
        packers[0].GetProperty("packerName").GetString().Should().Be("Alice");
        packers[0].GetProperty("orderCount").GetInt32().Should().Be(3);
    }

    // FR-4: Repository failure -> outer catch -> "error" shape with no data property.
    [Fact]
    public async Task LoadDataAsync_RepositoryThrows_ReturnsError()
    {
        // Arrange
        _repo
            .Setup(r => r.GetPackedTodayByPackerAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var sut = MakeSut();

        // Act — must not throw (outer catch handles it)
        var result = await sut.LoadDataAsync();

        // Assert
        var doc = ToJsonDoc(result);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("error");
        root.GetProperty("error").GetString().Should().Be("Nepodařilo se načíst data balení");
        // The error shape OMITS data entirely (differs from FailedJobsTile's data = null).
        root.TryGetProperty("data", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Build the test project to confirm it compiles.**

```bash
dotnet build /home/user/worktrees/feature-3707-Coverage-Gap-Packaging-Packingstatstile-Shoptet-Ap/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Build succeeded`, `0 Error(s)`. If it fails on an unresolved type, re-check the `using` directives against the "Notes grounded in source" list above (namespaces are verified) — do not change production code.

- [ ] **Step 3: Run only the new test class and verify all four `[Fact]`s pass green.**

```bash
dotnet test /home/user/worktrees/feature-3707-Coverage-Gap-Packaging-Packingstatstile-Shoptet-Ap/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingStatsTileTests"
```

Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0` (3 `[Fact]` methods). These are characterization tests over already-correct code, so they pass on the first run. If FR-4 fails with "property `data` was found", the tile shape assumption is wrong — re-read `PackingStatsTile.cs` before adjusting; if FR-2 throws instead of returning, the inner catch was not exercised (check that `GetOrdersBeingPackedCountAsync`, not `GetPackedTodayByPackerAsync`, is the throwing mock).

- [ ] **Step 4: Run `dotnet format` on the test project (repo completion gate).**

```bash
dotnet format /home/user/worktrees/feature-3707-Coverage-Gap-Packaging-Packingstatstile-Shoptet-Ap/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes
```

Expected: no output / exit 0. If it reports formatting changes, run the same command without `--verify-no-changes` to apply them, then re-run `dotnet test` from Step 3 to confirm still green.

- [ ] **Step 5: Commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs
git commit -m "test: cover PackingStatsTile happy path, Shoptet isolation, and repo failure

Adds PackingStatsTileTests pinning the three LoadDataAsync response shapes
(success with counts + packers, graceful degradation when Shoptet throws,
and error when the repository throws), closing a 0% coverage gap. Test-only;
no production code changed.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_011SvHnfKA516PqFLniqLPMp"
```
