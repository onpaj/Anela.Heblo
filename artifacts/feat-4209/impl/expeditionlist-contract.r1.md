# Implementation: expeditionlist-contract

## What was implemented
Added the consumer-owned contract interface `IOrderStatusReader` in the
`ExpeditionList` module, narrowing the surface `ExpeditionList` handlers need
from `ShoptetOrders.IEshopOrderClient` down to the single
`GetOrderStatusIdAsync` method they actually use. This mirrors the
`packaging-contract` task's approach and is the first of the tasks that
decouple `ExpeditionList` from directly consuming `IEshopOrderClient`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs` — new consumer-owned contract interface with a single `GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)` method. The doc comment explicitly preserves the existing 404 (`HttpRequestException` with `StatusCode == HttpStatusCode.NotFound`) contract that `PrintExpeditionOrderHandler` depends on, and requires implementations to let it propagate unmodified.

## Tests
No tests required for this task — it adds a currently-unused interface (no implementation or consumer wired up yet; that happens in `expeditionlist-adapter` and `expeditionlist-handler`).

## How to verify
```
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: Build succeeded, 0 errors (pre-existing warnings in unrelated files are unaffected).

Build was run and confirmed: 0 Errors, 135 pre-existing warnings unrelated to this change.

## Notes
Followed the task-context file's Step 1 code exactly, matching the
`IPackedOrderStatusUpdater` pattern established in `packaging-contract`. No
deviations.

## PR Summary
Added `IOrderStatusReader`, a consumer-owned contract interface in
`ExpeditionList.Contracts`, exposing only the single
`GetOrderStatusIdAsync` method that `ExpeditionList` handlers need — the
first step in decoupling `ExpeditionList` from directly injecting
`ShoptetOrders.IEshopOrderClient`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs` — new file, consumer-owned contract interface

## Status
DONE
