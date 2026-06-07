Spec written to `artifacts/feat-arch-review-shoptetorders-packaging-modu/spec.md`. It covers:

- **FR-1**: Add `IsEligibleForPacking` to `PackingOrder`, computed in `ShoptetApiPackingOrderClient`; remove the duplicated rule from both handlers.
- **FR-2**: Add `MarkAsPackedAsync` to `IEshopOrderClient`, implemented in `ShoptetOrderClient` via the existing `UpdateStatusAsync` path.
- **FR-3**: `ScanPackingOrderHandler` no longer references `ShoptetOrdersSettings`.
- **FR-4**: Byte-for-byte behaviour parity for both endpoints.

Status: `COMPLETE` — no open questions; the brief was unambiguous and the contract surface needed already exists.