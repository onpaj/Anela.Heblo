# Specification: DataQuality-Catalog Module Boundary Decoupling for ProductPairingDqtComparer

## Summary
`ProductPairingDqtComparer` in the DataQuality module directly imports and depends on four Catalog-owned domain types (`IEshopStockClient`, `IErpStockClient`, `EshopStock`, `ErpStock`, `ProductType`), violating this repository's module-boundary rule that cross-module reads must go through a consumer-owned contract implemented by a provider-side adapter. This spec introduces two DataQuality-owned contracts (`IDqtEshopStockSource`, `IDqtErpStockSource`) backed by DataQuality-owned snapshot DTOs, two Catalog-side adapters that implement them by delegating to the existing Catalog stock clients, rewires `ProductPairingDqtComparer` to depend only on the new contracts, and removes the now-unnecessary allowlist entries from the architecture boundary test.

## Background
The codebase enforces module independence via `ModuleBoundariesTests.cs`, a reflection-based test suite that fails CI when a consumer module's types reference a provider module's namespace outside an explicit, commented allowlist entry. The established remediation pattern — first proven by `ILeafletKnowledgeSource`/`KnowledgeBaseLeafletSourceAdapter`, and already applied twice within the DataQuality module itself (`IInvoiceShoptetSource`/`IInvoiceErpClient` for Invoices, and `IStockOperationQuery`/`IStockTakingQuery`/`IMaterialLotStockQuery` for Catalog) — is: the consumer module declares a narrow, consumption-shaped interface plus its own snapshot DTOs in its `Contracts/` folder; the provider module implements that interface with an adapter in its own `Infrastructure/` folder that maps its internal types onto the consumer's DTOs; the provider module registers the DI binding in its own `{Module}.cs`.

`ProductPairingDqtComparer` is the last unresolved violation of this kind for the DataQuality→Catalog boundary. `ModuleBoundariesTests.cs` (lines 128–144) already carries a `DataQualityCatalogAllowlist` with a comment explicitly describing this exact follow-up:

> "Track follow-up: introduce DataQuality-owned `IProductPairingQuery` contract and Catalog-side adapter that surfaces eshop/erp product snapshots without leaking Catalog types."

This spec implements that follow-up (using two focused contracts rather than one combined `IProductPairingQuery`, matching the granularity of the sibling `IInvoiceShoptetSource`/`IInvoiceErpClient` pair, since the eshop and ERP sides are independently sourced and independently mockable in tests).

## Functional Requirements

### FR-1: DataQuality-owned contracts and snapshot DTOs
Add two new files to `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/`:

- `IDqtEshopStockSource.cs`:
  ```csharp
  public interface IDqtEshopStockSource
  {
      Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken);
  }
  ```
- `IDqtErpStockSource.cs`:
  ```csharp
  public interface IDqtErpStockSource
  {
      Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken);
  }
  ```
- `DqtEshopStockItem.cs` — DataQuality-owned snapshot class (not a record, per project DTO rule) with exactly the fields `ProductPairingDqtComparer` consumes from `EshopStock` today: `Code` (`string`), `PairCode` (`string`), `Name` (`string`).
- `DqtErpStockItem.cs` — DataQuality-owned snapshot class with: `ProductCode` (`string`), `ProductName` (`string`), `IsSellable` (`bool`). `IsSellable` replaces the raw `ProductTypeId` field: the current comparer's private `IsSellable(ErpStock)` helper compares `ProductTypeId` against `(int)ProductType.Goods` / `(int)ProductType.Product` — that comparison is Catalog domain knowledge (the `ProductType` enum) and must move into the Catalog-side adapter (FR-2), not stay in DataQuality. DataQuality only ever needs the boolean outcome.

**Acceptance criteria:**
- Both interfaces and both DTOs live under `Anela.Heblo.Application.Features.DataQuality.Contracts`.
- Neither interface nor DTO references any `Anela.Heblo.Domain.Features.Catalog*` or `Anela.Heblo.Application.Features.Catalog*` namespace.
- DTOs are plain classes with `{ get; set; }` properties (matches `DqtEshopStockItem`/`DqtErpStockItem` sibling style used by `IssuedInvoiceDetail` usage and the project-wide "DTOs are classes, never records" rule).
- No speculative members are added beyond what `ProductPairingDqtComparer` actually reads (per the guideline's "no speculative methods" rule already documented in `development_guidelines.md`).

### FR-2: Catalog-side adapters
Add two new files to `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/`:

- `DataQualityEshopStockSourceAdapter.cs`:
  ```csharp
  internal sealed class DataQualityEshopStockSourceAdapter : IDqtEshopStockSource
  {
      private readonly IEshopStockClient _inner;
      public DataQualityEshopStockSourceAdapter(IEshopStockClient inner) => _inner = inner;

      public async Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken)
      {
          var products = await _inner.ListAsync(cancellationToken);
          return products
              .Select(p => new DqtEshopStockItem { Code = p.Code, PairCode = p.PairCode, Name = p.Name })
              .ToList();
      }
  }
  ```
- `DataQualityErpStockSourceAdapter.cs`:
  ```csharp
  internal sealed class DataQualityErpStockSourceAdapter : IDqtErpStockSource
  {
      private readonly IErpStockClient _inner;
      public DataQualityErpStockSourceAdapter(IErpStockClient inner) => _inner = inner;

      public async Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken)
      {
          var products = await _inner.ListAsync(cancellationToken);
          return products
              .Select(p => new DqtErpStockItem
              {
                  ProductCode = p.ProductCode,
                  ProductName = p.ProductName,
                  IsSellable = p.ProductTypeId == (int)ProductType.Goods || p.ProductTypeId == (int)ProductType.Product,
              })
              .ToList();
      }
  }
  ```
  This is where the `ProductType` enum comparison — moved verbatim from the current `ProductPairingDqtComparer.IsSellable` private method — now lives, since it is Catalog domain knowledge.

**Acceptance criteria:**
- Both adapter classes are `internal sealed`, matching `DataQualityStockOperationQueryAdapter` and `InvoiceErpClientAdapter` conventions.
- Both live under `Anela.Heblo.Application.Features.Catalog.Infrastructure`.
- Adapters perform pure delegation/mapping — no new business logic beyond the field projection and the pre-existing sellability check.
- Adapters are the only place in the codebase outside `Anela.Heblo.Domain.Features.Catalog*` that references `EshopStock`, `ErpStock`, or `ProductType` for this use case.

### FR-3: DI registration
In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, add two registrations alongside the existing DataQuality-adapter block (near `IStockOperationQuery`/`IStockTakingQuery`/`IMaterialLotStockQuery`/`IDqtResilienceService`, using the same `AddScoped` pattern and comment style):

```csharp
// DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
services.AddScoped<IDqtEshopStockSource, DataQualityEshopStockSourceAdapter>();
services.AddScoped<IDqtErpStockSource, DataQualityErpStockSourceAdapter>();
```

**Acceptance criteria:**
- Registrations are added to `CatalogModule.AddCatalogModule`, not to `DataQualityModule.AddDataQualityModule` (DI binding ownership follows the provider, per the documented pattern).
- `Scoped` lifetime is correct for both: `IEshopStockClient` is registered via `AddHttpClient<IEshopStockClient, ShoptetStockClient>` (transient-by-default typed client) and `IErpStockClient` is registered `AddSingleton`; a `Scoped` adapter consuming either is not a captive-dependency violation (Scoped may depend on Transient or Singleton).
- The application starts and resolves `ProductPairingDqtComparer` (itself `AddScoped` in `DataQualityModule.cs`) without a DI resolution error.

### FR-4: Rewire `ProductPairingDqtComparer`
Modify `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`:
- Remove `using Anela.Heblo.Domain.Features.Catalog;` and `using Anela.Heblo.Domain.Features.Catalog.Stock;`.
- Replace constructor parameters `IEshopStockClient eshopStockClient` → `IDqtEshopStockSource eshopStockSource`, `IErpStockClient erpStockClient` → `IDqtErpStockSource erpStockSource` (field names/types updated to match).
- Replace `List<EshopStock> eshopProducts` / `IReadOnlyList<ErpStock> erpProducts` locals with `IReadOnlyList<DqtEshopStockItem>` / `IReadOnlyList<DqtErpStockItem>`.
- The two `_resilienceService.ExecuteWithResilienceAsync(...)` call sites are updated to call `_eshopStockSource.ListAsync(...)` / `_erpStockSource.ListAsync(...)` — the resilience-wrapping structure, operation names (`"ProductPairingDqtComparer.EshopList"`, `"ProductPairingDqtComparer.ErpList"`), and exception/logging handling are otherwise unchanged.
- `sellableErpProducts = erpProducts.Where(IsSellable).ToList()` becomes `erpProducts.Where(p => p.IsSellable).ToList()`; the private `static bool IsSellable(ErpStock product)` helper method is deleted (its logic now lives in `DataQualityErpStockSourceAdapter`, FR-2).
- All other logic (pairing checks, mismatch construction, `Details` message text, `TotalChecked` computation) is unchanged — this is a pure type-substitution refactor, not a behavior change.

**Acceptance criteria:**
- `ProductPairingDqtComparer.cs` contains no `using` directive for `Anela.Heblo.Domain.Features.Catalog` or `Anela.Heblo.Domain.Features.Catalog.Stock`, and no reference to `EshopStock`, `ErpStock`, `IEshopStockClient`, `IErpStockClient`, or `ProductType` anywhere in the file.
- Existing behavior for both drift checks (Shoptet→ERP resolution via `PairCode`/`Code`, ERP→Shoptet sellable-product presence) is preserved exactly: same `DriftMismatch` fields, same `ProductPairingMismatch` flag combinations, same `TotalChecked` formula (union of Shoptet identifiers and sellable ERP codes).
- `dotnet build` succeeds with no new warnings.

### FR-5: Update the module-boundary allowlist and existing unit tests
- In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, remove the four `DataQualityCatalogAllowlist` entries for `ProductPairingDqtComparer` (lines ~134–143) and update the preceding comment block (lines 128–131) to state the violation is resolved (mirroring the existing "Empty — ..." comment style used for `LeafletAllowlist`, `ArticleAllowlist`, etc.). Leave the rest of `DataQualityCatalogAllowlist` untouched if any other entries exist under it (verify at implementation time — as of this writing, `ProductPairingDqtComparer` entries are the entirety of that allowlist).
- In `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`, update all test setup to mock `IDqtEshopStockSource`/`IDqtErpStockSource` (returning `DqtEshopStockItem`/`DqtErpStockItem` instances) instead of `IEshopStockClient`/`IErpStockClient` (returning `EshopStock`/`ErpStock`). The `_resilienceMock` setups' generic type arguments change from `List<EshopStock>`/`IReadOnlyList<ErpStock>` to `IReadOnlyList<DqtEshopStockItem>`/`IReadOnlyList<DqtErpStockItem>`. Test cases themselves (inputs, expected mismatches, expected `TotalChecked`) are unchanged — only the mocked type shifts, e.g. `ProductTypeId = 1` (Goods) becomes `IsSellable = true`.

**Acceptance criteria:**
- `dotnet test` for `Anela.Heblo.Tests` passes, including `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces` for the `"DataQuality -> Catalog"` rule with an allowlist that is now empty (or free of `ProductPairingDqtComparer` entries) for this rule.
- All pre-existing `ProductPairingDqtComparerTests` cases pass unmodified in intent (same scenarios, same assertions on `Mismatches`/`TotalChecked`), only rebound to the new mock types.
- No other test file references `ProductPairingDqtComparer`'s old constructor signature.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The adapters add one in-memory `Select(...).ToList()` projection per call over lists that are already fully materialized by the underlying Catalog clients (typically low thousands of SKUs); this is negligible relative to the existing HTTP/DB round trip inside `IEshopStockClient`/`IErpStockClient`. No new network calls, no new caching layer, no change to `DriftDqtJobRunner`'s scheduling or timeout behavior.

### NFR-2: Security
No change in security posture. No new external inputs, no new authentication/authorization surface. The adapters run in-process and carry no data beyond what already flows through `ProductPairingDqtComparer` today.

### NFR-3: Maintainability (module isolation)
This is the primary driver of the change: after it lands, DataQuality can be unit-tested and reasoned about without any compile-time dependency on Catalog's domain model, and a future change to `EshopStock`/`ErpStock`/`ProductType` (e.g. renaming a field unrelated to product pairing) can no longer break `ProductPairingDqtComparer` at compile time. This is enforced going forward by `ModuleBoundariesTests`.

## Data Model
No persisted data model changes — this is an in-memory contract/adapter refactor only, no new database tables, columns, or migrations.

New in-memory types (DataQuality-owned, `Contracts/` folder):

| Type | Kind | Fields |
|---|---|---|
| `IDqtEshopStockSource` | interface | `ListAsync(CancellationToken) : Task<IReadOnlyList<DqtEshopStockItem>>` |
| `IDqtErpStockSource` | interface | `ListAsync(CancellationToken) : Task<IReadOnlyList<DqtErpStockItem>>` |
| `DqtEshopStockItem` | class | `Code: string`, `PairCode: string`, `Name: string` |
| `DqtErpStockItem` | class | `ProductCode: string`, `ProductName: string`, `IsSellable: bool` |

New in-memory types (Catalog-owned, `Infrastructure/` folder):

| Type | Kind | Implements | Wraps |
|---|---|---|---|
| `DataQualityEshopStockSourceAdapter` | `internal sealed class` | `IDqtEshopStockSource` | `IEshopStockClient` |
| `DataQualityErpStockSourceAdapter` | `internal sealed class` | `IDqtErpStockSource` | `IErpStockClient` |

## API / Interface Design
No public/external API changes. This is an internal backend refactor; no controller, MediatR request/response, or frontend contract is touched. The only "interface design" is the two new C# contracts described in FR-1, consumed exclusively by `ProductPairingDqtComparer` and implemented exclusively by the two new Catalog adapters.

Call flow (unchanged shape, new types):
```
DriftDqtJobRunner
  → ProductPairingDqtComparer.CompareAsync(from, to, ct)
      → IDqtResilienceService.ExecuteWithResilienceAsync(() => IDqtEshopStockSource.ListAsync(ct), ...)
          → DataQualityEshopStockSourceAdapter.ListAsync(ct)
              → IEshopStockClient.ListAsync(ct)   [Catalog, unchanged]
      → IDqtResilienceService.ExecuteWithResilienceAsync(() => IDqtErpStockSource.ListAsync(ct), ...)
          → DataQualityErpStockSourceAdapter.ListAsync(ct)
              → IErpStockClient.ListAsync(ct)     [Catalog, unchanged]
```

## Dependencies
- Depends on the existing `IEshopStockClient` and `IErpStockClient` Catalog abstractions and their DI registrations (`ShoptetStockClient` via `AddHttpClient`, `FlexiStockClient` via `AddSingleton`) — unchanged by this work.
- Depends on the existing `IDqtResilienceService` (already a DataQuality-owned contract, implemented by `DataQualityResilienceAdapter` in Catalog) — unchanged, reused as-is.
- No new NuGet packages, no new external services, no new configuration.

## Out of Scope
- Any behavior change to the product-pairing drift-detection logic itself (mismatch rules, `Details` message wording, `TotalChecked` formula).
- Decoupling any other DataQuality comparer or any other module pair — this spec addresses only the `DataQualityCatalogAllowlist` entries tied to `ProductPairingDqtComparer`.
- Introducing a combined `IProductPairingQuery` single contract (the allowlist comment's suggested name) — two focused contracts (`IDqtEshopStockSource`, `IDqtErpStockSource`) are used instead, matching this repo's established granularity for the sibling Invoices decoupling. Renaming is a non-functional choice; see Open Questions.
- Any change to `IEshopStockClient`, `IErpStockClient`, `EshopStock`, `ErpStock`, or `ProductType` themselves.
- Performance optimization of the stock-listing calls (e.g. caching, pagination) — out of scope for this boundary fix.
- Any change to how `DriftDqtJobRunner` schedules or invokes `IDriftDqtComparer` implementations.

## Open Questions
None.
