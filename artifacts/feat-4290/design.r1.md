# Design: Fix MarketingPerformance → Invoices module-boundary violation (`IssuedInvoiceMonthlyRevenueSource`)

## Component Design

### 1. `IssuedInvoiceMonthlyRevenueSource` (relocated adapter)
- **Current location:** `backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs`, namespace `Anela.Heblo.Persistence.Marketing`.
- **New location:** `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceMonthlyRevenueSource.cs`, namespace `Anela.Heblo.Persistence.Invoices`.
- **Responsibility:** unchanged — implements `IMonthlyRevenueSource.GetAsync(YearMonth, CancellationToken)` by querying `ApplicationDbContext.IssuedInvoices` (`AsNoTracking()`, grouping, currency/wholesale rules) and returning a `MonthlyRevenueSnapshot`. Class body, constructor, private constants, and XML doc are copied verbatim.
- **Shape/visibility:** stays `public`, stays a direct-`DbContext` Persistence-layer class (does **not** get normalized into the `internal sealed` `Application/Features/Invoices/Infrastructure` adapter shape used by `InvoiceConsumptionSourceAdapter`/`InvoiceImportStatisticsSourceAdapter`) — this class already sits alongside `IssuedInvoiceRepository` in `Persistence/Invoices/` and follows that sibling's shape (direct EF Core access, provider owns it, no repository-interface indirection). Per arch-review Decision 1, forcing it into the Application-layer adapter shape would require extending `IIssuedInvoiceRepository` and is out of scope.
- **Added:** a short explanatory comment above the class noting it implements MarketingPerformance's `IMonthlyRevenueSource` contract, consistent with the comment style already in `InvoicesModule.cs` for the other two cross-module adapters.
- **Contract (unchanged):**
  ```csharp
  public interface IMonthlyRevenueSource
  {
      Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken);
  }
  ```
  This interface itself is not moving — it stays in `Anela.Heblo.Domain.Features.MarketingPerformance`, owned by the consumer module, per the established inversion pattern.

### 2. `InvoicesModule.AddInvoicesModule` (DI registration owner — gains a registration)
- Add `using Anela.Heblo.Domain.Features.MarketingPerformance;` (for `IMonthlyRevenueSource`); `using Anela.Heblo.Persistence.Invoices;` is already present.
- Add, alongside the two existing cross-module adapter registrations:
  ```csharp
  // Cross-module contract: Invoices implements MarketingPerformance's IMonthlyRevenueSource
  // via an adapter. DI registration owned by provider (Invoices), not consumer
  // (MarketingPerformance) — mirrors the IInvoiceConsumptionSource/IInvoiceImportStatisticsSource
  // pattern above.
  services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
  ```
- This becomes the third entry in `InvoicesModule.cs`'s existing list of provider-owned cross-module bindings.

### 3. `MarketingPerformanceModule.AddMarketingPerformanceModule` (DI registration owner — loses a registration)
- Remove `services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();`.
- Remove the now-unused `using Anela.Heblo.Persistence.Marketing;` import.
- No other change; `MarketingPerformanceRefreshService` (the sole consumer) continues to inject `IMonthlyRevenueSource` and is unaware which module supplied the binding, since DI resolution is registration-order-independent for a single binding.

### 4. Test infrastructure: `ModuleBoundariesTests.cs` (new regression guard)
Per arch-review Decision 2 / Specification Amendment 1, `ModuleBoundaryRule` supports exactly one `InspectedNamespacePrefix` and one `InspectedAssembly` per rule — it cannot express a single three-prefix rule. Implement as **two** rules, mirroring the existing `Analytics (Application) -> Invoices` / `Analytics (Domain) -> Invoices` pair:

- **`"MarketingPerformance (Application) -> Invoices"`**
  - `InspectedNamespacePrefix`: `"Anela.Heblo.Application.Features.MarketingPerformance"`
  - `InspectedAssembly`: default (`"Anela.Heblo.Application"`)
  - `ForbiddenNamespacePrefixes`: `["Anela.Heblo.Domain.Features.Invoices", "Anela.Heblo.Application.Features.Invoices", "Anela.Heblo.Persistence.Invoices"]`
  - Allowlist: new empty `MarketingPerformanceInvoicesAllowlist` (`HashSet<string>`), same style as `DataQualityInvoicesAllowlist`.

- **`"MarketingPerformance (Domain) -> Invoices"`**
  - `InspectedNamespacePrefix`: `"Anela.Heblo.Domain.Features.MarketingPerformance"`
  - `InspectedAssembly`: `"Anela.Heblo.Domain"`
  - Same `ForbiddenNamespacePrefixes` and allowlist as above.

Do not add `Anela.Heblo.Persistence.Marketing` as a third inspected prefix — no existing rule in this file treats a `Persistence.*` assembly as "inspected" (only ever as a forbidden target), and after the move, `Persistence/Marketing/` contains zero references to `Invoices` (only the file being relocated referenced it), so no third rule is needed. Both rules go in the `Rules` list near the other `-> Invoices` entries (`PackingMaterials -> Invoices`, `Analytics (Application/Domain) -> Invoices`, `DataQuality -> Invoices`).

### 5. Test fallout (not covered by spec.r1.md FR text, required for compilation)
- `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs` currently has `using Anela.Heblo.Persistence.Marketing;` — update to `using Anela.Heblo.Persistence.Invoices;` as part of the FR-1 move (required for `dotnet build` to succeed; the class no longer exists in the old namespace). Relocating the test file itself to `Features/Invoices/` is optional and not required.
- `MarketingPerformanceRefreshServiceTests.cs` also references `Anela.Heblo.Persistence.Marketing`, but that usage is for `MarketingPerformanceRepository` (not moving) — no change needed there.

## Data Schemas

No schema, entity, or DTO changes of any kind.

- **Database:** no migration. `IssuedInvoice` (owned by `Anela.Heblo.Domain.Features.Invoices`) is read-only from this adapter, unchanged shape and unchanged query (`AsNoTracking()`, grouping, currency/wholesale rules against `IssuedInvoices`).
- **Domain types:** `MonthlyRevenueSnapshot` and `YearMonth` (owned by `Anela.Heblo.Domain.Features.MarketingPerformance`) are unchanged.
- **Internal C# contract:** `IMonthlyRevenueSource.GetAsync(YearMonth month, CancellationToken cancellationToken) : Task<MonthlyRevenueSnapshot>` — signature, namespace, and ownership (consumer-owned, in `Domain.Features.MarketingPerformance`) are unchanged.
- **Public API surface:** none affected. No MediatR request/response, no controller, no OpenAPI/TypeScript client regeneration needed.
- **Event payloads:** none exist for this component; none introduced.

The only artifacts changing are: one file's path/namespace, one DI registration's owning module, and two new architecture-test rules (plus one test file's `using` statement). No wire-format, storage-format, or contract-shape change anywhere.
