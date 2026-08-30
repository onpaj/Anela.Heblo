# Specification: DataQuality-owned invoice snapshot contracts (decouple from Invoices domain types)

## Summary
`IInvoiceShoptetSource` and `IInvoiceErpClient` — consumer contracts owned by the DataQuality module — currently leak Invoices-module domain types (`IssuedInvoiceDetailBatch`, `IssuedInvoiceSourceQuery`, `IssuedInvoiceDetail`, `IssuedInvoiceDetailItem`, `InvoicePrice`) through their method signatures, and `InvoiceDqtComparer` consumes those types directly. This creates a compile-time dependency from DataQuality onto `Anela.Heblo.Domain.Features.Invoices`, violating the consumer-owns-the-contract pattern documented for `ILeafletKnowledgeSource`. This spec defines DataQuality-owned snapshot types that carry only the fields the module actually reads, moves the Invoices→DataQuality field mapping into the Invoices-side provider adapters, and closes the architecture-test allowlist that currently permits the leak.

## Background
`InvoiceDqtComparer` (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtComparer.cs`) reconciles issued invoices between Shoptet (via `IInvoiceShoptetSource`) and the ERP/FlexiBee system (via `IInvoiceErpClient`), both interfaces declared in DataQuality's own `Contracts/` folder. Despite being correctly *located* in DataQuality, both interfaces `using Anela.Heblo.Domain.Features.Invoices;` and expose Invoices' domain types verbatim in their signatures:

- `IInvoiceShoptetSource.GetAllAsync(IssuedInvoiceSourceQuery, ct) : Task<List<IssuedInvoiceDetailBatch>>`
- `IInvoiceErpClient.GetAllAsync(DateOnly from, DateOnly to, ct) : Task<List<IssuedInvoiceDetail>>`
- `InvoiceDqtComparer` additionally imports the same namespace directly to read `IssuedInvoiceDetailItem`, `InvoicePrice`, and `IssuedInvoiceDetailBatch.Invoices`.

The provider side is already correctly structured: `InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter` (`backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/`) implement these DataQuality contracts and are registered by `InvoicesModule.AddInvoicesModule`, per the documented `ILeafletKnowledgeSource` pattern (`docs/architecture/development_guidelines.md`, "Cross-Module Communication Example"). Only the *shape of the contract* is wrong: it should expose DataQuality-owned types so the Shoptet/ERP provider(s) could be swapped without touching DataQuality's compilation unit, and so DataQuality does not carry a compile-time reference to the entire Invoices domain surface (addresses, customer info, billing/shipping methods, etc. that `InvoiceDqtComparer` never reads).

This gap is already tracked in the codebase: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` defines a `DataQuality -> Invoices` `ModuleBoundaryRule` with a non-empty `DataQualityInvoicesAllowlist` (7 entries) and an explicit code comment: *"Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters."* This spec is that follow-up. Sibling allowlists for already-fixed violations (`LeafletAllowlist`, `ArticleAllowlist`, `SmartsuppKnowledgeBaseAllowlist`) are empty `HashSet`s with a comment noting the violation was closed — this fix should leave `DataQualityInvoicesAllowlist` in the same empty state.

`InvoiceDqtComparer` reads exactly these fields today (verified by inspection):
- Invoice level: `Code`, `Price.TotalWithVat`, `Price.TotalWithoutVat`, `Items`
- Item level: `Code`, `Amount`, `ItemPrice.WithVat`, `ItemPrice.WithoutVat`
- Batch level: only `IssuedInvoiceDetailBatch.Invoices` is read (flattened via `SelectMany`); `BatchId` is never used by DataQuality.
- Query level: `InvoiceDqtComparer` builds `IssuedInvoiceSourceQuery` itself, setting only `RequestId`, `DateFrom`, `DateTo` (leaving `InvoiceId`/`Currency` at their defaults) — DataQuality never queries by single invoice.

## Functional Requirements

### FR-1: Define DataQuality-owned invoice snapshot types
Add three new types to `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/` (new file(s), e.g. `DqtInvoiceSnapshot.cs`), following the project rule that DTOs are classes, never records:

- `DqtInvoiceSourceQuery` — replaces `IssuedInvoiceSourceQuery` on the DataQuality boundary. Carries only what `InvoiceDqtComparer` sets today: `RequestId` (string), `DateFrom` (`DateOnly`), `DateTo` (`DateOnly`). Using `DateOnly` here (rather than `DateTime`) matches the `CompareAsync(DateOnly from, DateOnly to, ...)` signature and removes the `from.ToDateTime(TimeOnly.MinValue)` conversion currently done in the comparer only to satisfy `IssuedInvoiceSourceQuery.DateFrom`'s `DateTime?` type — the date→datetime conversion moves into the Invoices-side adapter, which is the correct owner of that mapping.
- `DqtInvoiceSnapshot` — one issued invoice as DataQuality needs it: `Code` (string), `TotalWithVat` (decimal), `TotalWithoutVat` (decimal), `Items` (`List<DqtInvoiceItem>`). No batch wrapper type is exposed on the contract (see FR-2 rationale) and no address/customer/billing fields.
- `DqtInvoiceItem` — one invoice line as DataQuality needs it: `Code` (string), `Amount` (decimal), `WithVat` (decimal), `WithoutVat` (decimal).

**Acceptance criteria:**
- Three classes exist under `Anela.Heblo.Application.Features.DataQuality.Contracts`, none of them a `record`.
- None of the three types, nor any file in `Anela.Heblo.Application.Features.DataQuality.Contracts`, has a `using Anela.Heblo.Domain.Features.Invoices;` (or fully-qualified reference to that namespace).
- Field set matches exactly what `InvoiceDqtComparer` reads today (see Background) — no speculative fields (e.g. no `OrderCode`, `Customer`, `BillingAddress`, `VariantName`, `ProductGuid`, `BuyPrice`, `IsNonStock`, `BatchId`).

### FR-2: Update `IInvoiceShoptetSource` to the DataQuality-owned shape
Change the signature to:
```csharp
Task<List<DqtInvoiceSnapshot>> GetAllAsync(DqtInvoiceSourceQuery query, CancellationToken ct = default);
```
The batch wrapper (`IssuedInvoiceDetailBatch`) is dropped from the contract entirely: `InvoiceDqtComparer` immediately flattens `shoptetBatches.SelectMany(b => b.Invoices)` and never uses `BatchId`, so the batch concept carries no information DataQuality needs — per the `ILeafletKnowledgeSource` pattern's "expose only the operations it actually consumes (no speculative methods)," the contract should not force a consumer-irrelevant grouping type either. Flattening moves into `InvoiceShoptetSourceAdapter` (FR-4).

Remove `using Anela.Heblo.Domain.Features.Invoices;` from `IInvoiceShoptetSource.cs`.

**Acceptance criteria:**
- `IInvoiceShoptetSource.cs` compiles with zero references to `Anela.Heblo.Domain.Features.Invoices`.
- `InvoiceDqtComparer`'s call site becomes `var shoptetInvoices = await _shoptetSource.GetAllAsync(shoptetQuery, ct);` (no `.SelectMany(b => b.Invoices)` needed after this change, since flattening happened in the adapter) — or an equivalent one-line simplification; either is acceptable as long as the comparer no longer references `IssuedInvoiceDetailBatch`.

### FR-3: Update `IInvoiceErpClient` to the DataQuality-owned shape
Change the signature to:
```csharp
Task<List<DqtInvoiceSnapshot>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct);
```
(Parameter list is unchanged — `from`/`to`/`ct` were never Invoices-owned types — only the return type changes.)

Remove `using Anela.Heblo.Domain.Features.Invoices;` from `IInvoiceErpClient.cs`.

**Acceptance criteria:**
- `IInvoiceErpClient.cs` compiles with zero references to `Anela.Heblo.Domain.Features.Invoices`.

### FR-4: Provider-side adapters map Invoices domain types → DataQuality snapshot types
Update `InvoiceShoptetSourceAdapter` and `InvoiceErpClientAdapter` (both in `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/`) — the provider side, which is allowed to reference both namespaces:

- `InvoiceShoptetSourceAdapter.GetAllAsync`: map the incoming `DqtInvoiceSourceQuery` to an `IssuedInvoiceSourceQuery` (`RequestId` passthrough, `DateFrom`/`DateTo` converted from `DateOnly` via `.ToDateTime(TimeOnly.MinValue)`, `InvoiceId` left null, `Currency` left at its "CZK" default), call the wrapped `IIssuedInvoiceSource.GetAllAsync`, flatten `List<IssuedInvoiceDetailBatch>` → `List<IssuedInvoiceDetail>` (`SelectMany(b => b.Invoices)`), then map each `IssuedInvoiceDetail` → `DqtInvoiceSnapshot`.
- `InvoiceErpClientAdapter.GetAllAsync`: call the wrapped `IIssuedInvoiceClient.GetAllAsync(from, to, ct)` unchanged, then map each returned `IssuedInvoiceDetail` → `DqtInvoiceSnapshot`.
- Both adapters need the same `IssuedInvoiceDetail → DqtInvoiceSnapshot` / `IssuedInvoiceDetailItem → DqtInvoiceItem` mapping. Factor it once (e.g. a private static method or an internal extension method in `Anela.Heblo.Application.Features.Invoices.Infrastructure`) rather than duplicating it — this is provider-owned mapping code, not a DataQuality concern, so it must not live in `Contracts/` or in DataQuality's `Services/` folder.
- Mapping: `Code → Code`, `Price.TotalWithVat → TotalWithVat`, `Price.TotalWithoutVat → TotalWithoutVat`, `Items[].Code → Code`, `Items[].Amount → Amount`, `Items[].ItemPrice.WithVat → WithVat`, `Items[].ItemPrice.WithoutVat → WithoutVat`.

**Acceptance criteria:**
- Both adapter classes still implement their respective DataQuality contract interfaces and remain `internal sealed`.
- DI registrations in `InvoicesModule.AddInvoicesModule` (`AddSingleton<IInvoiceShoptetSource, InvoiceShoptetSourceAdapter>()`, `AddScoped<IInvoiceErpClient, InvoiceErpClientAdapter>()`) and their documented lifetime rationale (mirroring `IIssuedInvoiceSource`/`IIssuedInvoiceClient` lifetimes) are unchanged by this fix.
- A unit/integration test (new or existing) confirms the adapters produce a `DqtInvoiceSnapshot` whose `TotalWithVat`/`TotalWithoutVat`/`Items` values match the source `IssuedInvoiceDetail`'s `Price.TotalWithVat`/`Price.TotalWithoutVat`/`Items` for a representative invoice, including one with multiple items.

### FR-5: `InvoiceDqtComparer` consumes only DataQuality-owned types
Update `InvoiceDqtComparer.cs` so every reference to `IssuedInvoiceDetail`, `IssuedInvoiceDetailBatch`, `IssuedInvoiceDetailItem`, `IssuedInvoiceSourceQuery`, and `InvoicePrice` is replaced with `DqtInvoiceSnapshot`, `DqtInvoiceItem`, and `DqtInvoiceSourceQuery` respectively. Remove `using Anela.Heblo.Domain.Features.Invoices;` from the file. Comparison logic itself (tolerance-based total/item diffing, duplicate-code detection and grouping, missing-in-Shoptet/missing-in-Flexi detection) is unchanged — only the types it operates on change.

**Acceptance criteria:**
- `InvoiceDqtComparer.cs` has zero references to `Anela.Heblo.Domain.Features.Invoices`.
- All existing behavior is preserved bit-for-bit: tolerance constant (`0.02m`), duplicate-code grouping-not-throwing behavior, duplicate-item-code-within-invoice handling, all `InvoiceMismatchType` flag combinations, and `Details`/`ShoptetValue`/`FlexiValue` message formats are unchanged.

### FR-6: Close the `DataQuality -> Invoices` architecture-test allowlist
Update `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:
- Empty `DataQualityInvoicesAllowlist` (remove all 7 entries), matching the pattern already used for `LeafletAllowlist`, `ArticleAllowlist`, and `SmartsuppKnowledgeBaseAllowlist` once their underlying violations were fixed.
- Update the allowlist's leading comment block (currently: *"Shared invoice domain DTOs are referenced on the contracts and inside the comparer... Follow-up: extract a DataQuality-owned snapshot DTO and map in the adapters."*) to state the violation is closed, consistent with the sibling allowlists' "Empty — ... " comment style.
- Leave the `ModuleBoundaryRule` itself (`InspectedNamespacePrefix`, `ForbiddenNamespacePrefixes` covering `Anela.Heblo.Domain.Features.Invoices`, `Anela.Heblo.Application.Features.Invoices`, `Anela.Heblo.Persistence.Invoices`) unchanged — it already correctly scopes the check to `Anela.Heblo.Application.Features.DataQuality`.

**Acceptance criteria:**
- `DataQualityInvoicesAllowlist` is `new(StringComparer.Ordinal)` with no entries.
- The `DataQuality -> Invoices` `ModuleBoundaryTests` test method passes with the emptied allowlist (i.e., the reflection scan finds zero actual references from `Anela.Heblo.Application.Features.DataQuality` types to the three forbidden namespace prefixes).
- No other `ModuleBoundaryRule` or allowlist in the file is touched (in particular `DataQualityCatalogAllowlist`, which covers a separate, out-of-scope `ProductPairingDqtComparer → Catalog` violation, is left exactly as-is).

### FR-7: Update existing unit tests to the new contract shape
`backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtComparerTests.cs` currently mocks `IInvoiceShoptetSource`/`IInvoiceErpClient` and builds `IssuedInvoiceDetail`/`IssuedInvoiceDetailItem`/`IssuedInvoiceDetailBatch`/`InvoicePrice` test fixtures directly (`MakeInvoice`, `MakeItem` helpers, `SetupShoptet`/`SetupFlexi`). Update these helpers to build `DqtInvoiceSnapshot`/`DqtInvoiceItem` and mock the interfaces' new return types (`List<DqtInvoiceSnapshot>` directly, no batch wrapper). All 14 existing test cases must continue to pass with equivalent assertions (same invoice codes, mismatch types, tolerance values, `Details` substrings).

**Acceptance criteria:**
- `InvoiceDqtComparerTests.cs` has zero references to `Anela.Heblo.Domain.Features.Invoices` (its `using` for that namespace is removed).
- All 14 existing `[Fact]` test methods pass unchanged in intent (same scenario, same assertions), only the fixture-construction types change.
- New or updated adapter-level tests from FR-4 live under `backend/test/Anela.Heblo.Tests/Features/Invoices/` (or wherever `InvoiceShoptetSourceAdapter`/`InvoiceErpClientAdapter` tests currently live, if any exist — create the test file(s) if none exist yet).

## Non-Functional Requirements

### NFR-1: Performance
This is a pure refactor of DTO shapes and mapping location; it must not add extra I/O, database round-trips, or Shoptet/ERP API calls. The invoice comparison job (`InvoiceDqtJobRunner`, run on a schedule — see `DataQualityModule`) must retain its existing throughput characteristics: mapping `IssuedInvoiceDetail → DqtInvoiceSnapshot` is an O(n) in-memory projection per invoice/item and must not introduce N+1 calls or per-invoice allocations beyond what the existing `Select`/`SelectMany` LINQ already does.

### NFR-2: Security
No change in security posture: both contracts remain internal, in-process, read-only interfaces consumed only by `InvoiceDqtComparer` inside the DataQuality module. No new external surface, no new PII exposure — if anything, exposure is reduced, since `DqtInvoiceSnapshot`/`DqtInvoiceItem` no longer transit `IssuedInvoiceDetail`'s customer/address fields (`Customer`, `BillingAddress`, `DeliveryAddress`) through DataQuality's call stack at all.

### NFR-3: Maintainability / architecture-boundary enforcement
After this change, `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`'s `DataQuality -> Invoices` rule must run with a fully empty allowlist, so it becomes a hard, zero-tolerance CI gate: any future PR that reintroduces an Invoices-domain reference from DataQuality code fails the build immediately, with no allowlist escape hatch left to (re)populate. This mirrors the enforcement level already achieved for `Leaflet -> KnowledgeBase`, `Article -> KnowledgeBase`, and `Smartsupp -> KnowledgeBase`.

## Data Model

```
DqtInvoiceSourceQuery                 (DataQuality-owned, in Contracts/)
├── RequestId       : string
├── DateFrom         : DateOnly
└── DateTo           : DateOnly

DqtInvoiceSnapshot                    (DataQuality-owned, in Contracts/)
├── Code             : string
├── TotalWithVat     : decimal
├── TotalWithoutVat  : decimal
└── Items            : List<DqtInvoiceItem>

DqtInvoiceItem                        (DataQuality-owned, in Contracts/)
├── Code             : string
├── Amount           : decimal
├── WithVat          : decimal
└── WithoutVat       : decimal
```

Mapping performed in Invoices-side adapters (provider owns the mapping):
```
IssuedInvoiceDetail                    →  DqtInvoiceSnapshot
  .Code                                →   .Code
  .Price.TotalWithVat                  →   .TotalWithVat
  .Price.TotalWithoutVat               →   .TotalWithoutVat
  .Items[]                             →   .Items[]

IssuedInvoiceDetailItem                →  DqtInvoiceItem
  .Code                                →   .Code
  .Amount                              →   .Amount
  .ItemPrice.WithVat                   →   .WithVat
  .ItemPrice.WithoutVat                →   .WithoutVat

IssuedInvoiceDetailBatch.Invoices[]    →  flattened into List<DqtInvoiceSnapshot>
  (.BatchId is dropped — unused by DataQuality)

DqtInvoiceSourceQuery                  →  IssuedInvoiceSourceQuery
  .RequestId                           →   .RequestId
  .DateFrom (DateOnly)                 →   .DateFrom = DateFrom.ToDateTime(TimeOnly.MinValue)
  .DateTo (DateOnly)                   →   .DateTo = DateTo.ToDateTime(TimeOnly.MinValue)
  (n/a)                                →   .InvoiceId = null, .Currency = "CZK" (defaults)
```

Not part of the DataQuality-owned model (deliberately excluded, per FR-1): `OrderCode`, `CreationTime`, `ChangeTime`, `DueDate`, `TaxDate`, `AddressesEqual`, `VarSymbol`, `ConstSymbol`, `SpecSymbol`, `BillingMethod`, `ShippingMethod`, `VatPayer`, `BillingAddress`, `DeliveryAddress`, `Customer`, item-level `Name`, `VariantName`, `ProductGuid`, `AmountUnit`, `BuyPrice`, `IsNonStock`, and the invoice-level `Vat`/`CurrencyCode`/`ExchangeRate`/`VatRate` fields of `InvoicePrice`.

## API / Interface Design

No HTTP/controller surface changes — this is an internal module-boundary contract change only.

```csharp
// backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceShoptetSource.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceShoptetSource
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DqtInvoiceSourceQuery query,
        CancellationToken ct = default);
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IInvoiceErpClient.cs
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IInvoiceErpClient
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
```

Call flow (unchanged control flow, changed types crossing the boundary):
```
InvoiceDqtJobRunner
  → InvoiceDqtComparer.CompareAsync(from: DateOnly, to: DateOnly, ct)
      → IInvoiceShoptetSource.GetAllAsync(DqtInvoiceSourceQuery, ct)
          → [provider] InvoiceShoptetSourceAdapter
              → IIssuedInvoiceSource.GetAllAsync(IssuedInvoiceSourceQuery, ct) : List<IssuedInvoiceDetailBatch>
              → flatten + map → List<DqtInvoiceSnapshot>
      → IInvoiceErpClient.GetAllAsync(DateOnly, DateOnly, ct)
          → [provider] InvoiceErpClientAdapter
              → IIssuedInvoiceClient.GetAllAsync(DateOnly, DateOnly, ct) : List<IssuedInvoiceDetail>
              → map → List<DqtInvoiceSnapshot>
      → compare List<DqtInvoiceSnapshot> vs List<DqtInvoiceSnapshot> → InvoiceDqtComparisonResult
```

## Dependencies
- No new external libraries or services.
- Depends on the existing, unmodified `IIssuedInvoiceSource` and `IIssuedInvoiceClient` Invoices-module interfaces (the adapters' wrapped dependencies) — these are not touched by this change.
- Depends on the existing `ModuleBoundaryRule`/`DataQualityInvoicesAllowlist` scaffolding already present in `ModuleBoundariesTests.cs`, which this change is expected to close out rather than introduce from scratch.
- No database schema or migration impact (`IssuedInvoiceRepository`/`ApplicationDbContext` are untouched).

## Out of Scope
- The pre-existing, separately-tracked `DataQuality -> Catalog` violation in `ProductPairingDqtComparer` (`DataQualityCatalogAllowlist`) — explicitly called out in that allowlist's own comment as a distinct follow-up, not addressed here.
- Any change to `IIssuedInvoiceSource`, `IIssuedInvoiceClient`, or their own implementations (Shoptet API client, FlexiBee/ERP client) — this fix only changes the DataQuality-facing contract shape and the adapter mapping layer.
- Any change to `InvoiceDqtJobRunner`'s scheduling, `InvoiceMismatchType`, `InvoiceDqtComparisonResult`, or how comparison results are surfaced/reported downstream.
- Introducing a shared-kernel invoice DTO reusable by other consumers (e.g. `PackingMaterials`' `IInvoiceConsumptionSource` or `Analytics`' `IInvoiceImportStatisticsSource`, which have their own separate consumer-owned contracts) — each consumer module keeps its own snapshot type per the established pattern; this spec does not consolidate them.
- Renaming or restructuring `IssuedInvoiceDetail`/`IssuedInvoiceDetailItem`/`InvoicePrice` on the Invoices-domain side — those remain exactly as they are; only what crosses the DataQuality boundary changes.

## Open Questions
None.

## Status: COMPLETE
