# Design: Unit tests for PackingStatsTile failure isolation

## Component Design

**`PackingStatsTileTests`** (new, sealed) — `backend/test/Anela.Heblo.Tests/Features/Packaging/DashboardTiles/PackingStatsTileTests.cs`, namespace `Anela.Heblo.Tests.Features.Packaging.DashboardTiles`. No production code changes.

SUT is constructed directly (no DI container):

```
new PackingStatsTile(
    Mock<IPackageRepository>.Object,
    Mock<IPackingOrderClient>.Object,
    FakeTimeProvider,          // private nested TimeProvider subclass, not the framework FakeTimeProvider
    NullLogger<PackingStatsTile>.Instance)
```

Mocked dependencies and the calls each test configures:
- `IPackageRepository.GetPackedTodayByPackerAsync(fromUtc, toUtc, ct)` → `Task<(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)>`. Throws in FR-4.
- `IPackingOrderClient.GetOrdersBeingPackedCountAsync(ct)` → `Task<int>`. Throws in FR-2.
- `IPackingOrderClient.GetOrdersBeingProcessedCountAsync(ct)` → `Task<int>`.
- `TimeProvider` — reuse the nested `private sealed class FakeTimeProvider : TimeProvider` from `GetPackingDashboardHandlerTests`, pinned to a fixed non-UTC (Prague) offset, driving `GetLocalNow()` for the query window and `lastSync`/`lastUpdated`.
- `ILogger<PackingStatsTile>` — `NullLogger<PackingStatsTile>.Instance`.

Assertion path: `LoadDataAsync()` returns `Task<object>` (anonymous type). Serialize with `System.Text.Json` and read via `JsonDocument`/`JsonElement` (copy the `ToJsonDoc` helper from `FailedJobsTileTests` verbatim) — this is the only way to assert against an anonymous type, and it doubles as the true wire-contract check for the dashboard client. FluentAssertions used for the actual assertions.

Four test methods, one per spec FR, all `[Fact]`, `async Task`:
- `LoadDataAsync_AllDependenciesSucceed_ReturnsSuccessWithCountsAndPackers` (FR-1) — includes one `PackerPackingSummary` with `PackedBy = null` to also cover FR-3's `"Neznámý"` fallback in the same setup, per arch-review Decision 3.
- `LoadDataAsync_ShoptetClientThrows_ReturnsSuccessWithNullCountsAndPackersPopulated` (FR-2).
- `LoadDataAsync_RepositoryThrows_ReturnsError` (FR-4).
- (Optional/opportunistic, out of scope) partial-failure sub-case where only `GetOrdersBeingProcessedCountAsync` throws.

No new packages, no `.csproj` changes, no DI registration — `xunit`, `Moq`, `FluentAssertions`, `Microsoft.Extensions.TimeProvider.Testing` are already referenced by `Anela.Heblo.Tests`.

## Data Schemas

No schema changes; the tests assert against existing shapes, not new ones.

**Repository return tuple** (as returned by the mock):
```
(int TotalDistinctOrders, IReadOnlyList<PackerPackingSummary> ByPacker)
```

**`PackerPackingSummary`** (existing record, read-only for these tests):
```
PackerPackingSummary(Guid? PackedByUserId, string? PackedBy, int DistinctOrderCount)
```

**Serialized success response** (`status: "success"`), JSON keys verbatim from the anonymous object:
```
{
  "status": "success",
  "data": {
    "ordersBeingPackedCount": number | null,
    "ordersBeingProcessedCount": number | null,
    "ordersBeingPackedCountLastSync": string | null,   // ISO datetime, JsonValueKind.String when set
    "totalOrdersPackedToday": number,
    "packedByPacker": [
      { "packerId": string | null, "packerName": string, "orderCount": number }
    ]
  },
  "metadata": { "lastUpdated": string, "source": string },
  "drillDown": { "filters": ..., "enabled": bool, "tooltip": string }
}
```
- FR-1: all `data.*` fields populated from mocked repo total / packer list / client counts; null `PackedBy` maps to `packerName: "Neznámý"`.
- FR-2 (Shoptet throws): `status` stays `"success"`; `ordersBeingPackedCount`, `ordersBeingProcessedCount`, `ordersBeingPackedCountLastSync` all serialize as `JsonValueKind.Null`; `packedByPacker` and `totalOrdersPackedToday` remain fully populated (proves isolation).

**Serialized error response** (`status: "error"`), repository-failure path (FR-4):
```
{
  "status": "error",
  "error": "Nepodařilo se načíst data balení"
}
```
Note: `data` is **absent**, not `null` — assert with `root.TryGetProperty("data", out _).Should().BeFalse()`, not a `ValueKind.Null` check (this differs from `FailedJobsTile` and is the one contract detail that would silently break if copy-pasted from that sibling test).
