All 7 implementation commits are in place. Here's the output summary:

---

# Implementation: Decouple InvoiceDqtComparer from Invoices Module Interfaces

## What was implemented

Replaced `InvoiceDqtComparer`'s direct dependencies on `IIssuedInvoiceSource` and `IIssuedInvoiceClient` (Invoices-module behavior interfaces) with two narrow, DataQuality-owned read contracts. The Invoices module provides `internal sealed` adapter classes that delegate to its existing implementations. DI bindings with correctly mirrored lifetimes (Singleton for Shoptet, Scoped for Flexi) were added to `InvoicesModule.cs`. A new `DataQuality → Invoices` module boundary rule was added to `ModuleBoundariesTests.cs` with an allowlist for the five shared domain DTOs.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs` — new DataQuality-owned read contract (single `GetAllAsync`)
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs` — new DataQuality-owned read contract (single `GetAllAsync`)
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs` — `internal sealed` adapter delegating to `IIssuedInvoiceSource`
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceErpClientAdapter.cs` — `internal sealed` adapter delegating to `IIssuedInvoiceClient`
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — added two DI registrations (Singleton + Scoped)
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs` — constructor types swapped to new contracts
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` — mock types updated to new contracts
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — 13th boundary rule added with allowlist

## Tests

- `InvoiceDqtComparerTests.cs` — 12 tests all pass with the new mock types
- `ModuleBoundariesTests` — 13 theory cases pass including the new `DataQuality → Invoices` rule
- Full test suite: 4324 passed (38 pre-existing Docker integration failures, unrelated to this work)

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

## Notes

- Adapter lifetimes correctly mirror wrapped services: `InvoiceShoptetSourceAdapter` is `Singleton` (mirrors `IIssuedInvoiceSource` in `Program.cs:119`), `InvoiceErpClientAdapter` is `Scoped` (mirrors `IIssuedInvoiceClient` in `FlexiAdapterServiceCollectionExtensions.cs:93`).
- Domain DTOs (`IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetailItem`, `InvoicePrice`) remain in `Anela.Heblo.Domain.Features.Invoices` and are allowlisted in the boundary rule — consistent with the `Catalog → Manufacture` precedent.
- No behavior changes to `InvoiceDqtComparer` — only type declarations changed.

## PR Summary

Decouples `InvoiceDqtComparer` from Invoices-module behavior interfaces by introducing two DataQuality-owned read contracts (`IInvoiceShoptetSource`, `IInvoiceErpClient`) and thin adapter implementations in the Invoices module. This restores consumer-owned-contract boundaries per the architecture guidelines and eliminates the ISP violation where DataQuality was forced to implement four unused write/transaction methods. A new `ModuleBoundariesTests` rule with an explicit shared-DTO allowlist enforces the boundary going forward.

### Changes
- `DataQuality/Contracts/IInvoiceShoptetSource.cs` — new single-method read contract
- `DataQuality/Contracts/IInvoiceErpClient.cs` — new single-method read contract
- `Invoices/Infrastructure/InvoiceShoptetSourceAdapter.cs` — pure-delegation adapter (Singleton)
- `Invoices/Infrastructure/InvoiceErpClientAdapter.cs` — pure-delegation adapter (Scoped)
- `Invoices/InvoicesModule.cs` — two new DI registrations with lifetime-mirroring
- `DataQuality/Services/InvoiceDqtComparer.cs` — constructor updated to consume new contracts
- `Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` — mocks updated to new contract types
- `Tests/Architecture/ModuleBoundariesTests.cs` — 13th boundary rule with shared-DTO allowlist

## Status

DONE