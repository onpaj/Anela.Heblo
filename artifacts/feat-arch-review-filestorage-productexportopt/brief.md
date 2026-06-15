## Module
FileStorage

## Finding
`ProductExportOptions` is configured in the API layer's shared extension method rather than inside `FileStorageModule`:

```csharp
// backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:364
services.Configure<ProductExportOptions>(configuration.GetSection("ProductExportOptions"));
```

`FileStorageModule.AddFileStorageModule` already accepts `IConfiguration` and registers several services, but it does not bind its own options. To wire `ProductExportOptions` correctly today, you must look in two unrelated files: the module and the API extension class.

## Why it matters
ADR-004 establishes that each vertical slice owns its full DI wiring in one file (`{Feature}Module.cs`). Splitting a module's registrations across two layers makes it easy to miss the binding when copying/porting the module, causes merge-conflict risk on `ServiceCollectionExtensions.cs` (it already accumulates cross-module registrations), and contradicts the documented "Module Registration" pattern in `development_guidelines.md`.

## Suggested fix
Move the `services.Configure<ProductExportOptions>(...)` call into `FileStorageModule.AddFileStorageModule` (where the `IConfiguration configuration` parameter is already available), and remove it from `ServiceCollectionExtensions.cs`. One-line change per file.

---
_Filed by daily arch-review routine on 2026-06-05._