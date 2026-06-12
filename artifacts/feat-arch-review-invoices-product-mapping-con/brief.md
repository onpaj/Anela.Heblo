## Module
Invoices

## Finding
`InvoicesModule.cs` lines 57–58 register a product-mapping transformation with product codes baked directly into the DI factory lambda:

```csharp
services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
    new ProductMappingIssuedInvoiceImportTransformation("1287", "SLU000001"));
```

`"1287"` is a Shoptet product code and `"SLU000001"` is an ABRA Flexi product code. These are business-domain values, not infrastructure wiring.

## Why it matters
- Changing either product code requires modifying C# source, rebuilding, and redeploying — there is no way to update it through configuration.
- DI registration files are not where business-rule constants are expected to live; reviewers and maintainers will not look there when the mapping breaks.
- `ProductMappingIssuedInvoiceImportTransformation` likely already accepts the codes as constructor parameters specifically to keep them injectable, yet the only call site defeats that by hard-coding the values.

## Suggested fix
Introduce an options class, e.g. `ProductMappingOptions` with `ShoptetCode` and `ErpCode` properties, bind it from `appsettings.json` via `IOptions<ProductMappingOptions>`, and inject it into the factory:

```csharp
// appsettings.json
"ProductMapping": {
  "ShoptetCode": "1287",
  "ErpCode": "SLU000001"
}

// InvoicesModule.cs
services.AddTransient<IIssuedInvoiceImportTransformation>(provider => {
    var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;
    return new ProductMappingIssuedInvoiceImportTransformation(opts.ShoptetCode, opts.ErpCode);
});
```

The actual secret/value goes into Azure Key Vault if it is environment-specific, or into the shared `appsettings.json` if it is stable across environments. Either way it is no longer in compiled code.

---
_Filed by daily arch-review routine on 2026-06-09._