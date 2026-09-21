# Code Review: expeditionlist-adapter

## Summary
The implementation matches the task-context specification exactly: a new internal
`ShoptetOrdersOrderStatusReaderAdapter` delegates `IOrderStatusReader.GetOrderStatusIdAsync` to
`IEshopOrderClient.GetOrderStatusIdAsync`, is registered as Transient in `ShoptetOrdersModule`
mirroring the existing `IPackedOrderStatusUpdater` pattern, and is covered by two tests (happy
path delegation and unmodified propagation of the 404 `HttpRequestException`). Both tests pass.

## Review Result: PASS

### task: expeditionlist-adapter
**Status:** PASS

## Docs to Update
(None — this is an internal DI wiring change following an already-established pattern; no public
behavior, CLI, or setup docs are affected.)

## Overall Notes
- Signature (`Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)`)
  matches both `IOrderStatusReader` and `IEshopOrderClient` exactly.
- The 404 propagation test correctly locks in the contract documented on `IOrderStatusReader`
  (callers such as `PrintExpeditionOrderHandler` depend on the exact `HttpRequestException`
  shape).
- DI registration comment style and Transient lifetime consistently mirror the sibling
  `IPackedOrderStatusUpdater` registration, keeping the cross-module-contract ownership pattern
  consistent across both adapters in this module.
