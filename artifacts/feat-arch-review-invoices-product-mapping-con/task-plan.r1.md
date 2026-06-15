Self-review passed — all 6 FRs trace to specific tasks, no placeholders, types/method signatures (`ProductMappingOptions.ShoptetCode`/`ErpCode`/`SectionName`, `AddInvoicesModule(IServiceCollection, IConfiguration)`, `InvoicesModuleTests`) stay consistent across Tasks 1→5.

Plan saved to `docs/superpowers/plans/2026-06-11-externalize-product-mapping-codes.md`.

**Summary of the 6-task implementation plan:**

1. **Task 1** — Create `ProductMappingOptions.cs` POCO with `[Required]` `ShoptetCode` and `ErpCode` plus `SectionName = "ProductMapping"`.
2. **Task 2** — Write a failing wiring test (TDD red) — fails at compile because `AddInvoicesModule` doesn't take `IConfiguration` yet.
3. **Task 3** — Refactor `InvoicesModule` to accept `IConfiguration`, bind options with `.ValidateDataAnnotations().ValidateOnStart()`, switch the product-mapping factory to resolve `IOptions<ProductMappingOptions>`; update `ApplicationModule.cs:95`. Test passes (TDD green).
4. **Task 4** — Add `"ProductMapping": { "ShoptetCode": "1287", "ErpCode": "SLU000001" }` to `appsettings.json` adjacent to `InvoiceImport`, then verify `WebApplicationFactory`-based integration tests still boot.
5. **Task 5** — Add FR-5 startup-validation tests proving `OptionsValidationException` fires on missing section and empty `ShoptetCode`.
6. **Task 6** — Regression gate: assert no `"1287"`/`"SLU000001"` literals remain in `InvoicesModule.cs`, run the pre-existing transformation tests + full backend suite, run `dotnet format`.

Each task ends with its own commit so progress is bisectable. The arch review's two amendments (FR-3 test-settings audit, FR-6 validation-test mechanism) are addressed inline: `HebloWebApplicationFactory` layers `appsettings.Test.json` on top of `appsettings.json` (verified by reading the factory at line 39), so no separate test-settings edit is needed; and the validation tests use `provider.GetRequiredService<IOptions<...>>().Value` to trigger `DataAnnotation` validation without requiring `IHost.StartAsync()`.