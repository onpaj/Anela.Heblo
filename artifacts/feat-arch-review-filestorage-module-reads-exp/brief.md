## Module
FileStorage

## Finding
`FileStorageModule.cs` line 20 reads the Azure Blob Storage connection string using a key that belongs to the ExpeditionList module:

```csharp
var connectionString = configuration["ExpeditionList:BlobConnectionString"];
```

The `ExpeditionList` config section and the `BlobConnectionString` key within it (`appsettings.json:522–532`) are owned by the ExpeditionList module (see `ExpeditionList/PrintPickingListOptions.cs`). FileStorage reaching into that section creates a hidden runtime coupling: if ExpeditionList ever renames its config key or section, the blob storage service silently falls back to the development storage emulator with no warning.

## Why it matters
This violates the module independence rule from `development_guidelines.md`: "No direct references between feature modules" and "Communication only through contracts/interfaces." Configuration namespaces are effectively a module's public API surface — reading another module's config key is the same class of violation as reading another module's database table directly.

## Suggested fix
1. Add a dedicated `FileStorage:BlobConnectionString` key to `appsettings.json` (and to all environment-specific files / Azure Key Vault).
2. Change `FileStorageModule.cs:20` to read `configuration["FileStorage:BlobConnectionString"]`.
3. Optionally, introduce a `FileStorageOptions` class to hold the key and bind it in `FileStorageModule.AddFileStorageModule`, replacing the raw `configuration[...]` lookup.

---
_Filed by daily arch-review routine on 2026-06-05._