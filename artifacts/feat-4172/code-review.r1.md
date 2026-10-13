## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs:47` and `:72` — the `_repositoryMock.Setup(...).ReturnsAsync(...)` call is duplicated between the `Fact` and the `Theory`; both could share a small private setup helper (e.g. `SetupOrders(IEnumerable<PurchaseOrder> orders)`), matching the reduction already applied to order-building via `BuildOrderWithAmount`.
