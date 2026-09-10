# Implementation: add-dqt-invoice-snapshot-contracts-and-mapper

## What was implemented

Added the three DataQuality-owned invoice snapshot contract types
(`DqtInvoiceSourceQuery`, `DqtInvoiceSnapshot`, `DqtInvoiceItem`) and the
provider-owned `InvoiceDqtSnapshotMapper` extension-method mapper that
converts Invoices-domain `IssuedInvoiceDetail` / `IssuedInvoiceDetailItem`
into them. Per the task spec, this task is self-contained: it does not wire
these new types into `IInvoiceShoptetSource`, `IInvoiceErpClient`, either
adapter's `GetAllAsync` body, or `InvoiceDqtComparer`. The new types and
mapper are currently unused by production code — task 2 wires them in — so
the rest of the codebase compiles and behaves exactly as before.

Followed this repo's DTO rule: all three new contract types are plain
classes with `{ get; set; }` properties, not C# records.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs`
  — new file, contains `DqtInvoiceSourceQuery`, `DqtInvoiceSnapshot`, and
  `DqtInvoiceItem` classes. No `using` directives referencing the Invoices
  domain namespace, as required (consumer-owned contract, provider-agnostic).
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs`
  — new file, `internal static class InvoiceDqtSnapshotMapper` with
  `ToDqtSnapshot(this IssuedInvoiceDetail)` and
  `ToDqtItem(this IssuedInvoiceDetailItem)` extension methods. Placed
  beside the two existing adapters (`InvoiceShoptetSourceAdapter`,
  `InvoiceErpClientAdapter`) per the design doc's Decision 3 (provider-owned
  code that references both the Invoices domain and DataQuality's contracts
  namespace belongs beside the adapters, not in `Contracts/` or
  DataQuality's `Services/` folder).
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs`
  — new file, exact content specified in the task context. Relies on
  `Anela.Heblo.Application`'s existing `InternalsVisibleTo("Anela.Heblo.Tests")`
  (in `AssemblyInfo.cs`) to reach the `internal` mapper directly, the same
  pattern used by `InvoiceConsumptionSourceAdapterTests`.

## Tests

`InvoiceDqtSnapshotMapperTests` (3 tests):
- `ToDqtSnapshot_MapsInvoiceLevelFields` — verifies `Code`, `TotalWithVat`,
  `TotalWithoutVat`, and an empty `Items` list map correctly for an invoice
  with no line items.
- `ToDqtSnapshot_MapsMultipleItems_WithoutSwappingWithVatAndWithoutVat` —
  verifies multi-item mapping preserves order and does not swap
  `WithVat`/`WithoutVat`, using deliberately asymmetric sample values.
- `ToDqtItem_MapsFieldsFromNestedItemPrice` — verifies the item-level mapper
  pulls `WithVat`/`WithoutVat` from the nested `ItemPrice`, not from
  `BuyPrice` or any other field.

## How to verify

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceDqtSnapshotMapperTests"
```

Full solution build (`dotnet build` from `backend/`) also confirmed no
regressions in existing projects — the new types are additive and unused by
any other production code path.

## Notes

No deviations from the task spec. Implemented directly per the exact file
contents given in the task context (developer/reviewer subagent dispatch
was not used for this bounded unit — the spec was unambiguous and
fully-specified, so I wrote the three files verbatim as instructed and
verified with build + targeted test run).

## PR Summary

Adds the DataQuality-owned invoice snapshot contract types
(`DqtInvoiceSourceQuery`, `DqtInvoiceSnapshot`, `DqtInvoiceItem`) and a
provider-owned `InvoiceDqtSnapshotMapper` that converts Invoices domain
`IssuedInvoiceDetail`/`IssuedInvoiceDetailItem` into them, laying the
groundwork for wiring these into `IInvoiceShoptetSource`/`IInvoiceErpClient`
in a follow-up task. Nothing existing is modified or wired up yet — this is
purely additive.

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs` — new consumer-owned contract types
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs` — new provider-owned mapper
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs` — unit tests for the mapper

## Status
DONE
