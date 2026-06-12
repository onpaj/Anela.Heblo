All 4 implementation commits are on the branch. Here is the output summary:

---

# Implementation: Externalize Product Mapping Codes for Invoice Import Transformation

## What was implemented

Moved the hard-coded Shoptet→ERP product code mapping (`"1287"` → `"SLU000001"`) out of `InvoicesModule.cs`'s DI factory lambda into a strongly-typed `ProductMappingOptions` class bound from `appsettings.json`, matching the Options pattern established by `MeetingTasksOptions` and peers. Startup validation via `.ValidateDataAnnotations().ValidateOnStart()` ensures the application fails fast with a clear error if the `ProductMapping` section is missing or its required fields are empty.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs` — new options POCO with `[Required]` `ShoptetCode`/`ErpCode` properties and `SectionName = "ProductMapping"` constant
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — signature changed to accept `IConfiguration`, options binding wired, factory lambda now resolves `IOptions<ProductMappingOptions>` instead of using literals
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — single-line update: `AddInvoicesModule()` → `AddInvoicesModule(configuration)`
- `backend/src/Anela.Heblo.API/appsettings.json` — `ProductMapping` section added adjacent to `InvoiceImport` with production-equivalent values
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs` — new test class with 4 tests: 2 wiring tests (happy path) and 2 startup-validation tests (OptionsValidationException scenarios)

## Tests

- `InvoicesModuleTests.cs` — 4 tests, all pass:
  - `AddInvoicesModule_BindsProductMappingOptions_AndTransformationUsesThem` — end-to-end: resolves the transformation, runs it against a test invoice, asserts code rewrite
  - `AddInvoicesModule_RegistersOptions_BoundFromConfiguration` — asserts IOptions resolves with configured values
  - `AddInvoicesModule_ThrowsOptionsValidationException_WhenProductMappingSectionMissing` — missing section → OptionsValidationException naming both fields
  - `AddInvoicesModule_ThrowsOptionsValidationException_WhenShoptetCodeEmpty` — empty ShoptetCode → OptionsValidationException
- `ProductMappingIssuedInvoiceImportTransformationTests.cs` — 6 pre-existing tests unchanged, all pass

## How to verify

```bash
# No literals remain in module
grep -c '"1287"\|"SLU000001"' backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
# Expected: 0

# All 4 new wiring/validation tests pass
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices.InvoicesModuleTests"

# Pre-existing transformation tests pass (unchanged)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductMappingIssuedInvoiceImportTransformationTests"

# Full invoice test suite (64 tests)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Invoices"
```

## Notes

No deviations from the spec. The `HebloWebApplicationFactory` layers `appsettings.Test.json` on top of `appsettings.json` (not replacing it), so the `ProductMapping` section in `appsettings.json` is inherited by all integration tests — no change to `appsettings.Test.json` was needed. All 64 invoice tests (including `InvoiceImportIntegrationTests`) passed without modification.

## PR Summary

Externalizes two hard-coded business reference values (`"1287"`, `"SLU000001"`) from `InvoicesModule.cs` into a `ProductMappingOptions` class bound from `appsettings.json`, following the established Options pattern used across all other feature modules. The product codes can now be changed via configuration without a rebuild. Startup validation via `[Required]` + `ValidateOnStart()` prevents silent registration of a transformation with empty codes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Invoices/ProductMappingOptions.cs` — new options POCO
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — signature update, options binding, factory refactored
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — pass `configuration` to `AddInvoicesModule`
- `backend/src/Anela.Heblo.API/appsettings.json` — `ProductMapping` section with production values
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoicesModuleTests.cs` — 4 new wiring/validation tests

## Status
DONE