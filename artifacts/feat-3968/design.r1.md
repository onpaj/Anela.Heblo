# Design: DataQuality-owned invoice snapshot contracts (decouple from Invoices domain types)

## Component Design

### `IInvoiceShoptetSource` (consumer contract, `DataQuality.Contracts`, unchanged location)
Owned by DataQuality. Responsibility: hand `InvoiceDqtComparer` the Shoptet-side issued invoices for a date range, in DataQuality's own shape. After this change it references only DataQuality-owned types and standard BCL types — zero references to `Anela.Heblo.Domain.Features.Invoices`.

- `Task<List<DqtInvoiceSnapshot>> GetAllAsync(DqtInvoiceSourceQuery query, CancellationToken ct = default)`
- No batch/grouping type on the contract — the caller receives a flat list, since `BatchId` carries no information DataQuality reads.

### `IInvoiceErpClient` (consumer contract, `DataQuality.Contracts`, unchanged location)
Owned by DataQuality. Responsibility: hand `InvoiceDqtComparer` the ERP/FlexiBee-side issued invoices for a date range, in the identical DataQuality shape as the Shoptet source.

- `Task<List<DqtInvoiceSnapshot>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct)`
- Parameter list unchanged from today (`from`/`to`/`ct` were never Invoices-owned); only the return type changes.

### `InvoiceDqtComparer` (`DataQuality.Services`, unchanged location)
Responsibility unchanged: reconcile Shoptet vs. ERP issued invoices for a date range (tolerance-based total/item diffing, duplicate-code detection and grouping, missing-in-Shoptet/missing-in-Flexi detection), producing `InvoiceDqtComparisonResult`. After this change it operates exclusively on `DqtInvoiceSnapshot`/`DqtInvoiceItem`/`DqtInvoiceSourceQuery` and has zero references to `Anela.Heblo.Domain.Features.Invoices`. It builds `DqtInvoiceSourceQuery` itself from its own `CompareAsync(DateOnly from, DateOnly to, ...)` parameters — no date-type conversion happens here anymore.

### `InvoiceShoptetSourceAdapter` (provider, `Invoices.Infrastructure`, unchanged location, `internal sealed`)
Implements `IInvoiceShoptetSource` on behalf of the Invoices module. Responsibility, expanded by this change: in addition to wrapping `IIssuedInvoiceSource`, it now owns the full boundary translation in both directions —
- inbound: maps `DqtInvoiceSourceQuery` → `IssuedInvoiceSourceQuery` (`RequestId` passthrough; `DateFrom`/`DateTo` converted `DateOnly → DateTime` via `.ToDateTime(TimeOnly.MinValue)`; `InvoiceId = null`; `Currency = "CZK"` default),
- outbound: calls `IIssuedInvoiceSource.GetAllAsync`, flattens `List<IssuedInvoiceDetailBatch>` → `List<IssuedInvoiceDetail>` via `SelectMany(b => b.Invoices)`, then maps each to `DqtInvoiceSnapshot` via the shared mapper.
DI registration and lifetime (`AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>()`, mirroring `IIssuedInvoiceSource`'s lifetime) are unchanged.

### `InvoiceErpClientAdapter` (provider, `Invoices.Infrastructure`, unchanged location, `internal sealed`)
Implements `IInvoiceErpClient`. Responsibility, expanded by this change: calls `IIssuedInvoiceClient.GetAllAsync(from, to, ct)` unchanged, then maps each returned `IssuedInvoiceDetail` to `DqtInvoiceSnapshot` via the shared mapper. DI registration and lifetime (`AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>()`, mirroring `IIssuedInvoiceClient`'s lifetime) are unchanged.

### `InvoiceDqtSnapshotMapper` (new, provider-owned, `Invoices.Infrastructure`)
New `internal static class` housing the one piece of mapping logic both adapters need, so it is written once rather than duplicated:
- `ToDqtSnapshot(this IssuedInvoiceDetail)` → `DqtInvoiceSnapshot`
- `ToDqtItem(this IssuedInvoiceDetailItem)` → `DqtInvoiceItem`

Lives beside the two adapters it serves (not in `Contracts/`, not in DataQuality's `Services/`) because it is provider-owned mapping code: it depends on both the Invoices domain namespace (source) and DataQuality's contracts namespace (target), which only provider-side code is permitted to do.

### Module boundary enforcement (`ModuleBoundariesTests.cs`, test-only)
`DataQualityInvoicesAllowlist` goes from 7 entries to empty (`new(StringComparer.Ordinal)`), joining `LeafletAllowlist`/`ArticleAllowlist`/`SmartsuppKnowledgeBaseAllowlist` as a zero-tolerance CI gate: any future reintroduction of `Anela.Heblo.Domain.Features.Invoices`, `Anela.Heblo.Application.Features.Invoices`, or `Anela.Heblo.Persistence.Invoices` from `Anela.Heblo.Application.Features.DataQuality` fails the build with no allowlist escape hatch. `DataQualityCatalogAllowlist` (separate, out-of-scope `ProductPairingDqtComparer → Catalog` violation) is untouched. The `ModuleBoundaryRule` definitions themselves (`InspectedNamespacePrefix`, `ForbiddenNamespacePrefixes`) are unchanged.

### Compile-time dependency direction (before / after)

```
Before:
  DataQuality.Contracts  ──uses──▶  Invoices.Domain        (the violation)
  DataQuality.Services   ──uses──▶  Invoices.Domain        (the violation)
  Invoices.Infrastructure ──implements──▶  DataQuality.Contracts

After:
  DataQuality.Contracts  ──uses──▶  (nothing in Invoices)
  DataQuality.Services   ──uses──▶  (nothing in Invoices)
  Invoices.Infrastructure ──implements──▶  DataQuality.Contracts
  Invoices.Infrastructure ──uses──▶  Invoices.Domain        (adapters still read their own domain types to map from — expected and unrestricted)
```

Only one cross-module arrow survives: `Invoices.Infrastructure → DataQuality.Contracts` — provider implements consumer's interface, never the reverse.

## Data Schemas

### New DataQuality-owned contract types
`backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs` — three plain classes (never records, per project DTO rule), no Invoices namespace reference anywhere in the file:

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public class DqtInvoiceSourceQuery
{
    public string RequestId { get; set; } = string.Empty;
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
}

public class DqtInvoiceSnapshot
{
    public string Code { get; set; } = string.Empty;
    public decimal TotalWithVat { get; set; }
    public decimal TotalWithoutVat { get; set; }
    public List<DqtInvoiceItem> Items { get; set; } = new();
}

public class DqtInvoiceItem
{
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal WithVat { get; set; }
    public decimal WithoutVat { get; set; }
}
```

Field set is exhaustive as listed — deliberately excludes everything `InvoiceDqtComparer` does not read: `OrderCode`, `CreationTime`, `ChangeTime`, `DueDate`, `TaxDate`, `AddressesEqual`, `VarSymbol`, `ConstSymbol`, `SpecSymbol`, `BillingMethod`, `ShippingMethod`, `VatPayer`, `BillingAddress`, `DeliveryAddress`, `Customer`, item-level `Name`, `VariantName`, `ProductGuid`, `AmountUnit`, `BuyPrice`, `IsNonStock`, `BatchId`, and the invoice-level `Vat`/`CurrencyCode`/`ExchangeRate`/`VatRate` fields of `InvoicePrice`.

### Contract interfaces (API shape of the module boundary)

```csharp
// DataQuality/Contracts/IInvoiceShoptetSource.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceShoptetSource
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DqtInvoiceSourceQuery query,
        CancellationToken ct = default);
}
```

```csharp
// DataQuality/Contracts/IInvoiceErpClient.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceErpClient
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

### Field-level mapping (provider-owned, lives in `InvoiceDqtSnapshotMapper` / adapter bodies)

```
IssuedInvoiceDetail                    →  DqtInvoiceSnapshot          [InvoiceDqtSnapshotMapper.ToDqtSnapshot]
  .Code                                →   .Code
  .Price.TotalWithVat                  →   .TotalWithVat
  .Price.TotalWithoutVat               →   .TotalWithoutVat
  .Items[] (mapped via ToDqtItem)      →   .Items[]

IssuedInvoiceDetailItem                →  DqtInvoiceItem              [InvoiceDqtSnapshotMapper.ToDqtItem]
  .Code                                →   .Code
  .Amount                              →   .Amount
  .ItemPrice.WithVat                   →   .WithVat
  .ItemPrice.WithoutVat                →   .WithoutVat

IssuedInvoiceDetailBatch.Invoices[]    →  flattened into List<DqtInvoiceSnapshot>   [InvoiceShoptetSourceAdapter only]
  (.BatchId is dropped — unused by DataQuality)

DqtInvoiceSourceQuery                  →  IssuedInvoiceSourceQuery    [InvoiceShoptetSourceAdapter only]
  .RequestId                           →   .RequestId
  .DateFrom (DateOnly)                 →   .DateFrom = DateFrom.ToDateTime(TimeOnly.MinValue)
  .DateTo (DateOnly)                   →   .DateTo = DateTo.ToDateTime(TimeOnly.MinValue)
  (n/a)                                →   .InvoiceId = null, .Currency = "CZK" (defaults)
```

Both invoice-shape mappings (`ToDqtSnapshot`, `ToDqtItem`) are shared code, defined once in `InvoiceDqtSnapshotMapper` and called from both `InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter`. The query mapping (`DqtInvoiceSourceQuery → IssuedInvoiceSourceQuery`) and the batch flatten exist only in `InvoiceShoptetSourceAdapter`, since `InvoiceErpClientAdapter`'s wrapped `IIssuedInvoiceClient.GetAllAsync(DateOnly, DateOnly, ct)` takes no query object and its source returns an already-flat list.

### End-to-end call flow (unchanged control flow, changed types crossing the DataQuality boundary)

```
InvoiceDqtJobRunner
  → InvoiceDqtComparer.CompareAsync(DateOnly from, DateOnly to, ct)
      builds DqtInvoiceSourceQuery { RequestId, DateFrom = from, DateTo = to }   (DateOnly, no conversion here)
      → IInvoiceShoptetSource.GetAllAsync(DqtInvoiceSourceQuery, ct)
          → [provider] InvoiceShoptetSourceAdapter
              maps DqtInvoiceSourceQuery → IssuedInvoiceSourceQuery
              → IIssuedInvoiceSource.GetAllAsync(IssuedInvoiceSourceQuery, ct) : List<IssuedInvoiceDetailBatch>
              → SelectMany(b => b.Invoices) → List<IssuedInvoiceDetail>
              → .Select(ToDqtSnapshot) → List<DqtInvoiceSnapshot>
      → IInvoiceErpClient.GetAllAsync(from, to, ct)
          → [provider] InvoiceErpClientAdapter
              → IIssuedInvoiceClient.GetAllAsync(from, to, ct) : List<IssuedInvoiceDetail>   (call unchanged)
              → .Select(ToDqtSnapshot) → List<DqtInvoiceSnapshot>
      → compare List<DqtInvoiceSnapshot> vs List<DqtInvoiceSnapshot>   (tolerance/dup/mismatch logic unchanged)
      → InvoiceDqtComparisonResult
```

No HTTP/controller surface, MediatR request/response, database schema, or migration is affected by any of the above — this is an internal, in-process module-boundary contract change only, and `InvoiceDqtComparisonResult`'s own shape is untouched.
