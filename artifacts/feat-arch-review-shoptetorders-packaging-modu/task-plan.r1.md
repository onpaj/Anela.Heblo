Plan complete and saved to `docs/superpowers/plans/2026-06-05-decouple-packaging-from-shoptet-orders-settings.md`.

The plan covers 10 tasks executed in TDD style with frequent commits:

1. **Task 1** — Add `IsEligibleForPacking` to `PackingOrder` DTO + tighten `StatusId` XML doc (arch-review amendment 2).
2. **Task 2** — Declare `MarkAsPackedAsync` on `IEshopOrderClient` (build red).
3. **Task 3** — Implement `MarkAsPackedAsync` in `ShoptetOrderClient` via `UpdateStatusAsync` (build green).
4. **Task 4** — `ShoptetApiPackingOrderClient` sets `IsEligibleForPacking` from `ShoptetOrdersSettings.PackingStateId`.
5. **Task 5** — Rewrite `GetPackingOrderHandlerTests` for the new contract (tests red).
6. **Task 6** — Simplify `GetPackingOrderHandler` to consume `order.IsEligibleForPacking` (tests green).
7. **Task 7** — Rewrite `ScanPackingOrderHandlerTests` (tests red, with `MarkAsPackedAsync` verifications).
8. **Task 8** — Simplify `ScanPackingOrderHandler`: drop `ShoptetOrdersSettings`, call `MarkAsPackedAsync`, update warning log.
9. **Task 9** — Add `Packaging -> ShoptetOrders` rule to `ModuleBoundariesTests` to pin the boundary (arch-review amendment 1).
10. **Task 10** — Full build/test/format/grep validation across the four allowed status-id locations (NFR-3).

The plan ends with a spec-coverage matrix mapping every FR/NFR and both arch-review amendments to specific tasks.