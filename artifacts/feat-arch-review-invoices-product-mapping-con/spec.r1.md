Now I have all the context I need to write a thorough spec.

# Specification: Externalize Product Mapping Codes for Invoice Import Transformation

## Summary
The `ProductMappingIssuedInvoiceImportTransformation` is currently registered in `InvoicesModule.cs` with two product codes (`"1287"` and `"SLU000001"`) hard-coded in the DI factory lambda. This spec extracts those business-domain values into a strongly-typed `ProductMappingOptions` class bound from `appsettings.json`, aligning the module with the established Options pattern already used elsewhere in the codebase (e.g., `MeetingTasksOptions`, `OrgChartOptions`, `CatalogCacheOptions`).

## Background
Daily architecture review flagged `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs:57-58`:

```csharp
services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
    new ProductMappingIssuedInvoiceImportTransformation("1287", "SLU000001"));
```

- `"1287"` is a Shoptet product code; `"SLU000001"` is an ABRA Flexi (ERP) product code.
- These are business mapping values, not infrastructure wiring; they have no business being in a DI registration file.
- Changing either code today requires editing C# source, rebuilding, and redeploying. There is no operational path to update the mapping when business needs change.
- The transformation class already accepts both codes via constructor parameters (`ProductMappingIssuedInvoiceImportTransformation.cs:11`), so the seam exists — the call site simply defeats it.
- The codebase already has a well-established Options pattern with `SectionName` constants, `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`, and dedicated `*Options.cs` files in each feature module. This change brings the Invoices module in line with that convention.

## Functional Requirements

### FR-1: Introduce `ProductMappingOptions` class
Create a new strongly-typed options class to hold the Shoptet→ERP product code mapping.

**Acceptance criteria:**
- New file `backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs` exists.
- Class name: `ProductMappingOptions`.
- Public `const string SectionName = "ProductMapping";`.
- Property `ShoptetCode` (string), marked `[Required]`, no default value other than `string.Empty` initializer.
- Property `ErpCode` (string), marked `[Required]`, no default value other than `string.Empty` initializer.
- File matches the style of `MeetingTasksOptions.cs` (uses `System.ComponentModel.DataAnnotations`, public class, properties with `get; set;`).

### FR-2: Bind `ProductMappingOptions` from configuration in `InvoicesModule`
Wire the options class into DI and bind it to the `ProductMapping` configuration section. Mirror the pattern used by `MeetingTasksModule`.

**Acceptance criteria:**
- `InvoicesModule.AddInvoicesModule` signature changes to `AddInvoicesModule(this IServiceCollection services, IConfiguration configuration)`.
- The call site in `ApplicationModule.cs:95` is updated to pass `configuration`.
- `services.AddOptions<ProductMappingOptions>().Bind(configuration.GetSection(ProductMappingOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart()` is invoked before the transformation registration.
- The `ProductMappingIssuedInvoiceImportTransformation` registration factory resolves `IOptions<ProductMappingOptions>` from the provider and passes `opts.ShoptetCode` and `opts.ErpCode` to the constructor.
- Hard-coded literals `"1287"` and `"SLU000001"` no longer appear anywhere in `InvoicesModule.cs`.

### FR-3: Add `ProductMapping` section to `appsettings.json`
Provide the production-equivalent values in the base configuration file so the application boots with the same mapping behavior as today.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.API/appsettings.json` includes a top-level `ProductMapping` section with `"ShoptetCode": "1287"` and `"ErpCode": "SLU000001"`.
- The section is placed near other Shoptet/Invoice-related sections (e.g., adjacent to `InvoiceImport`) for discoverability.
- No other `appsettings.*.json` file is changed (the existing values stay environment-agnostic and stable across environments — see Open Questions if this assumption is invalid).

### FR-4: Preserve runtime behavior
The change must be purely a refactor — invoice import behavior must be byte-for-byte identical to the current production behavior.

**Acceptance criteria:**
- After the change, importing an invoice with an item whose `Code == "1287"` still results in that item's code being rewritten to `"SLU000001"`.
- All other items are passed through unchanged.
- The transformation is still registered as `Transient` and as one of the `IIssuedInvoiceImportTransformation` instances enumerated by the import pipeline (registration order vs. siblings `GiftWithoutVATIssuedInvoiceImportTransformation` and `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation` is preserved).

### FR-5: Startup validation on misconfiguration
If the `ProductMapping` section is missing or its required fields are empty, the application must fail fast at startup with a clear, actionable error message, rather than silently registering a transformation with empty strings.

**Acceptance criteria:**
- `.ValidateDataAnnotations().ValidateOnStart()` is configured on the options binding.
- Removing `ProductMapping` from `appsettings.json` causes the application to throw `OptionsValidationException` (or equivalent) during startup, naming the missing fields.
- The error surfaces before the first invoice import is attempted.

### FR-6: Unit-test the wiring
Confirm via tests that the options are bound and consumed correctly.

**Acceptance criteria:**
- A new test (xUnit, in `backend/test/Anela.Heblo.Tests/Features/Invoices/`) builds a `ServiceCollection`, calls `AddInvoicesModule` with an `IConfiguration` containing a `ProductMapping` section with test values, resolves `IEnumerable<IIssuedInvoiceImportTransformation>`, and asserts that the `ProductMappingIssuedInvoiceImportTransformation` instance rewrites the configured `ShoptetCode` to the configured `ErpCode`.
- A second test asserts that omitting the `ProductMapping` section causes the host build / options validation to fail.
- Existing `ProductMappingIssuedInvoiceImportTransformationTests` (which test the transformation class directly with hard-coded `TEST001` / `NEW001`) remain unchanged and continue to pass — the transformation class's contract is not modified.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact. The options instance is resolved once per `Transient` instantiation of the transformation (one DI resolution per invoice import). `IOptions<T>` resolution is O(1) and cached by the host.

### NFR-2: Security
- The product codes are not secrets — they are business reference data. They belong in `appsettings.json`, not Azure Key Vault.
- No new attack surface is introduced.
- Validation prevents the transformation from running with empty/null codes, which would silently corrupt data.

### NFR-3: Maintainability
- Eliminates the inconsistency where one of three `IIssuedInvoiceImportTransformation` registrations uses a factory lambda with literals while the other two use the standard `AddTransient<TService, TImpl>()` overload.
- Makes the mapping discoverable by configuration reviewers who scan `appsettings.json`, not by source-code archaeologists who happen to open `InvoicesModule.cs`.
- Brings the Invoices module's DI signature in line with peer modules that take `IConfiguration` (the majority — see `ApplicationModule.cs:80-109`).

### NFR-4: Backward compatibility (configuration)
A clean cutover. The DI signature for `AddInvoicesModule` changes (no overload preserved), and the only known caller (`ApplicationModule.cs:95`) is updated in the same change. There are no external consumers of this extension method.

## Data Model

### `ProductMappingOptions`
| Property      | Type     | Required | Description                                                                 |
|---------------|----------|----------|-----------------------------------------------------------------------------|
| `ShoptetCode` | `string` | Yes      | Product code as it appears on incoming Shoptet invoice line items.          |
| `ErpCode`     | `string` | Yes      | Product code to substitute, matching the corresponding ABRA Flexi product.  |

Bound from configuration section `ProductMapping`. No persistence — values live entirely in configuration.

## API / Interface Design

No public HTTP API changes. No MediatR contract changes.

### Internal changes
- `InvoicesModule.AddInvoicesModule(this IServiceCollection)` → `InvoicesModule.AddInvoicesModule(this IServiceCollection, IConfiguration)`.
- `ApplicationModule.cs:95` updated: `services.AddInvoicesModule(configuration);`.

### Configuration schema (added to `appsettings.json`)
```json
"ProductMapping": {
  "ShoptetCode": "1287",
  "ErpCode": "SLU000001"
}
```

## Dependencies
- `Microsoft.Extensions.Options` and `Microsoft.Extensions.Configuration` — already transitively referenced by `Anela.Heblo.Application`.
- `System.ComponentModel.DataAnnotations` for `[Required]` — already used by `MeetingTasksOptions`.
- No new NuGet packages.

## Out of Scope
- Supporting **multiple** product-code mappings. The transformation class today maps exactly one Shoptet code to one ERP code, and the brief asks only to externalize those two values. Extending to a list of mappings (e.g., `"Mappings": [{ "ShoptetCode": "...", "ErpCode": "..." }, ...]`) is a separate feature.
- Reading the mapping from Key Vault. The codes are not secrets and the brief explicitly says `appsettings.json` is acceptable when stable across environments.
- Hot-reloading / `IOptionsMonitor`. The mapping is read once at transformation construction; runtime reconfiguration is not requested.
- UI for managing product mappings. Out of scope for this refactor.
- Refactoring the other two transformations (`GiftWithoutVATIssuedInvoiceImportTransformation`, `RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation`). They do not currently embed business literals in their DI registration and are not flagged by the brief.
- Renaming `InvoiceImport` config section or any unrelated invoice configuration.

## Open Questions
None.

## Status: COMPLETE