## Module / File
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`

## Coverage
Line coverage: 25.2% (filter threshold: 60%)
13 existing tests cover: BoxNotFound, New→Opened, Opened→InTransit, Opened→Quarantine, Quarantine→Received, InTransit→Received (including weight rounding and product-code aggregation), and Opened→New restore.

## What's not tested

**`HandleOpenToReserve` callback** (`Opened → Reserve` transition):
- The callback validates that `request.Location` is non-empty and returns `RequiredFieldMissing` with `field=Location` if it is. There is no test for this transition at all — neither the guard failure nor the happy path where a location is provided and the box proceeds to Reserve state.

**`Reserve → Received` path via `HandleReceived`**:
- The `CallBackMap` maps `(Reserve, Received)` to `HandleReceived`, the same callback used for `InTransit→Received` and `Quarantine→Received`. While the other two entry points are tested, the `Reserve→Received` path is not. The `HandleReceived` implementation aggregates box items by `ProductCode`, rounds fractional amounts, and creates a stock-up operation per distinct product code — behaviour that could differ in practice for boxes that went through Reserve state (e.g. partial-reserve items).

## Why it matters
If `HandleOpenToReserve` silently accepts a box without a location, reserved boxes become unlocalised — breaking the downstream "find reserved stock at location X" queries. The `Reserve→Received` gap means the receiving workflow for reserved shipments is entirely untested despite being a live code path.

## Suggested approach
- Add a test for `Opened→Reserve` with an empty `Location` — assert `RequiredFieldMissing` / `field=Location`.
- Add a test for `Opened→Reserve` with a valid `Location` — assert success and that `box.Location` is set.
- Add a test for `Reserve→Received` mirroring the existing `InTransit→Received` tests: box with items, assert stock-up operations are created with correct product codes and rounded amounts. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
