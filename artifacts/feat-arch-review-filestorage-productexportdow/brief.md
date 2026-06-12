## Module
FileStorage

## Finding
`ProductExportDownloadJob` (and its companion `ProductExportOptions`) live inside the `FileStorage` module:

```
backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs
backend/src/Anela.Heblo.Application/Features/FileStorage/ProductExportOptions.cs
```

The job's logic is entirely product-export domain: it knows the export URL (`ProductExportOptions.Url`), the target blob container for product data (`ProductExportOptions.ContainerName`), and the output filename format (`products_{timestamp}.csv`). None of this is generic file-storage infrastructure — it is Shoptet/Catalog business logic that happens to use `IBlobStorageService` as a transport.

As a result, the `FileStorage` module cannot be treated as a reusable, stable infrastructure module: changes to the product-export business rules (new URL, different filename convention, additional post-processing) require editing a module named "FileStorage." The module also carries `ProductExportOptions` in its root namespace, polluting what should be a generic infrastructure API.

## Why it matters
SRP — the module has two unrelated reasons to change: (1) how blobs are stored/retrieved, and (2) how product export downloads are scheduled and named. This split will grow over time if more domain-specific jobs are added here.

## Suggested fix
Move `ProductExportDownloadJob` and `ProductExportOptions` to the module that owns the product-export domain (currently `Catalog` or a dedicated `ShoptetOrders`/`Catalog` feature). The job can continue to inject `IBlobStorageService` (cross-module via the Domain interface — already the correct pattern). `FileStorageModule` registration of the job is then removed, and the receiving module's `Module.cs` registers it instead.

This is a move, not a rewrite — the handler logic is unchanged.

---
_Filed by daily arch-review routine on 2026-06-05._