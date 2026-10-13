## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

### Verification performed
- `dotnet build Anela.Heblo.sln` — 0 errors (256 pre-existing warnings, none introduced by this diff).
- `dotnet test` filtered to `Packaging|ExpeditionList|ModuleBoundariesTests|ShoptetOrders` — 376 passed, 0 failed.

### Notes
- Implementation matches `spec.r1.md` FR-1 through FR-7: `IPackedOrderStatusUpdater` (Packaging.Contracts)
  and `IOrderStatusReader` (ExpeditionList.Contracts) are new, narrow, consumer-owned interfaces with
  exactly one method each, matching `IEshopOrderClient`'s existing method signatures verbatim.
- `ShoptetOrdersPackedOrderStatusUpdaterAdapter` / `ShoptetOrdersOrderStatusReaderAdapter` live in
  `ShoptetOrders.Infrastructure`, are `internal sealed`, delegate 1:1 to `IEshopOrderClient`, and are
  registered as `Transient` in `ShoptetOrdersModule`, matching the lifetime of `IEshopOrderClient`'s own
  registration and the `IShipmentDeliveryChecker` precedent cited in the architecture review.
- `PrintExpeditionOrderHandler`, `ScanPackingOrderHandler`, and `CompletePackingOrderHandler` no longer
  reference `IEshopOrderClient` or any `ShoptetOrders` namespace member for this concern; the existing
  404 `HttpRequestException` handling in `PrintExpeditionOrderHandler` is preserved and the new adapter's
  own test (`GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified`) confirms the
  exception shape survives the adapter boundary unmodified.
- `ModuleBoundariesTests.cs` gained a new `ExpeditionList -> ShoptetOrders` rule (empty allowlist) and had
  the `IEshopOrderClient` allowlist entries removed from `Packaging -> ShoptetOrders`, correctly pinning
  FR-7's "no remaining direct ShoptetOrders coupling" requirement; the architecture test suite passes.
- Naming deviates slightly from the spec's suggested `EshopOrderClient*Adapter` names (actual:
  `ShoptetOrders*Adapter`) — this is a non-blocking naming choice consistent with the module-prefixed
  naming already used elsewhere in the codebase, not a functional issue.
- All 7 planned tasks (`packaging-contract`, `packaging-adapter`, `packaging-handlers`,
  `expeditionlist-contract`, `expeditionlist-adapter`, `expeditionlist-handler`,
  `module-boundary-enforcement`) passed their own developer/reviewer cycles at revision 1.

No correctness issues found. This is a pure, well-scoped internal refactor matching the spec and the
established consumer-owns-contract pattern in this codebase.
