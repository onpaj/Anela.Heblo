# Specification: Decouple FileStorage Module from ExpeditionList Configuration

## Summary
The `FileStorage` module currently reads its Azure Blob Storage connection string from a configuration key owned by the `ExpeditionList` module, creating a hidden cross-module coupling that violates the project's module independence rule. This specification defines the work required to give `FileStorage` its own configuration namespace, migrate all environments to the new key, and prevent silent fallback to the development storage emulator when the key is missing.

## Background
The project follows Clean Architecture with Vertical Slice organization. `docs/architecture/development_guidelines.md` explicitly forbids direct references between feature modules and requires communication only through contracts/interfaces. Configuration namespaces are part of a module's public API surface — reading another module's config key is the same class of violation as reading another module's database table directly.

Today, `FileStorageModule.cs:20` reads:

```csharp
var connectionString = configuration["ExpeditionList:BlobConnectionString"];
```

The `ExpeditionList:BlobConnectionString` key (defined in `appsettings.json:522–532`) is owned by `ExpeditionList` and is actively consumed by it: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:18-22` binds `IOptions<PrintPickingListOptions>` and uses both `BlobConnectionString` and `BlobContainerName` to construct the `BlobContainerClient` for `AzureBlobPrintQueueSink`. With `ExpeditionList:PrintSink` set to `"AzureBlob"` (`appsettings.json:534`), the sink is live in production. The key therefore stays where it is; this work is purely additive on the `FileStorage` side.

If ExpeditionList renames or restructures its config in the future, the blob storage service silently falls back to the development storage emulator (`UseDevelopmentStorage=true`) with no warning, which could result in data being written to the wrong location in production. This was filed by the daily arch-review routine on 2026-06-05.

## Functional Requirements

### FR-1: Introduce a dedicated `FileStorage` configuration section
The `FileStorage` module must read its Azure Blob Storage connection string from a key under its own configuration namespace.

**Acceptance criteria:**
- A new key `FileStorage:BlobConnectionString` exists in `appsettings.json` and any environment-specific configuration files (`appsettings.Development.json`, `appsettings.Staging.json`, `appsettings.Production.json` as applicable).
- The new key is provisioned in Azure Key Vault for **staging** (`kv-heblo-stg`) **and production** using the project's standard `--` separator: `FileStorage--BlobConnectionString`. The exact production vault name must be confirmed by the deploying engineer (likely `kv-heblo-prod` by naming convention) and recorded in the PR description.
- The value of the new key matches the connection string currently in use by `FileStorage` so that no behavioral change occurs for existing deployments.

### FR-2: Update `FileStorageModule` to read from its own key
`FileStorageModule.cs` must no longer reference the `ExpeditionList` configuration section.

**Acceptance criteria:**
- `FileStorageModule.cs:20` reads `configuration["FileStorage:BlobConnectionString"]` (or, preferably, an options-bound equivalent — see FR-3).
- A repository-wide search for `"ExpeditionList:BlobConnectionString"` returns zero matches inside the `FileStorage` module.
- `ExpeditionList` continues to read its own `ExpeditionList:BlobConnectionString` key without modification. The key remains owned by `ExpeditionList` and is consumed by `AzureAdapterModule.cs` for `AzureBlobPrintQueueSink`; do not remove, rename, or otherwise touch it.

### FR-3: Introduce a strongly-typed `FileStorageOptions` class
Replace the raw `configuration[...]` lookup with an options pattern binding to keep configuration access consistent with the rest of the codebase.

**Acceptance criteria:**
- A new `FileStorageOptions` class lives inside the `FileStorage` module and exposes a `BlobConnectionString` property.
- `FileStorageModule.AddFileStorageModule` binds the `FileStorage` configuration section to `FileStorageOptions` via `services.Configure<FileStorageOptions>(...)` or equivalent.
- The blob storage service resolves the connection string via `IOptions<FileStorageOptions>` (or a snapshot/monitor as appropriate) rather than reading from `IConfiguration` directly.
- `FileStorageOptions` is a class (not a C# record) per the project's DTO guideline.

### FR-4: Fail fast when configuration is missing
The current implementation silently falls back to `UseDevelopmentStorage=true` when the connection string key is absent. This must be replaced with explicit validation so misconfiguration surfaces at startup, not silently in production.

**Acceptance criteria:**
- When `FileStorage:BlobConnectionString` is missing or empty in a non-Development environment, the application fails fast at startup with a clear error message naming the missing key.
- In the `Development` environment, behavior may continue to default to `UseDevelopmentStorage=true` — but only when the key is *explicitly* set to `UseDevelopmentStorage=true` or absent **and** `IHostEnvironment.IsDevelopment()` is true. A warning is logged if the fallback is used.
- Validation runs at application startup (e.g., via `ValidateOnStart()` on the options binding or an `IValidateOptions<FileStorageOptions>` implementation), not lazily on first use.

### FR-5: Update environment provisioning documentation
All places that document or provision Azure Blob Storage configuration must be updated so future developers and operators add the new key, not the legacy one.

**Acceptance criteria:**
- `docs/architecture/environments.md` (or the appropriate environment doc) mentions the `FileStorage:BlobConnectionString` key and its Key Vault counterpart in both staging and production.
- Any deployment scripts, `azure-pipelines.yml`/GitHub Actions workflows, or local-dev setup scripts that reference `ExpeditionList:BlobConnectionString` for the purpose of FileStorage are updated. Scripts referencing `ExpeditionList:BlobConnectionString` for ExpeditionList's own use are left untouched.
- A short migration note is added to the staging and production deployment runbook (or equivalent) describing how the key was rolled out so on-call engineers can trace it.

### FR-6: Verify no other module reaches into FileStorage or ExpeditionList configuration
This change is an opportunity to confirm the cleanup is complete.

**Acceptance criteria:**
- A repository-wide grep for `configuration["ExpeditionList:` outside the `ExpeditionList` module returns zero matches.
- A repository-wide grep for `configuration["FileStorage:` outside the `FileStorage` module returns zero matches.
- Findings are recorded in the PR description.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact is expected. The change is configuration-only and runs once at startup. Options binding is materialized at DI container build time and reused.

### NFR-2: Security
- The new `FileStorage:BlobConnectionString` secret must be stored in Azure Key Vault for **both** staging (`kv-heblo-stg`) **and** production deployments per the project rule: *"All secrets go to Azure Key Vault, never to Web App environment variables."*
- The legacy `ExpeditionList:BlobConnectionString` Key Vault secret remains in place; it is actively consumed by `ExpeditionList` itself (see Background).
- Connection strings must never be logged in plaintext, including in startup validation error messages — the error should name the missing key, not include the offending value.

### NFR-3: Rollout safety — hard cutover
- **Strategy: hard cutover, secret first.** Provision the `FileStorage--BlobConnectionString` secret in Azure Key Vault for **every** target environment (staging `kv-heblo-stg` and production) **before** the code change is merged and deployed.
- No temporary fallback to `ExpeditionList:BlobConnectionString` is added to the new code path. A fallback would carry the exact coupling this spec removes into the new code and leave dead code to clean up later.
- The PR description must explicitly state that the Key Vault secret has been provisioned in all target environments before merge, and name the exact secret/vault used in each.

### NFR-4: Testability
- The options binding must be unit-testable using `ConfigurationBuilder` with an in-memory collection.
- The fail-fast validation behavior (FR-4) must be covered by at least one unit or integration test that confirms startup fails with a meaningful error when the key is missing in a non-Development environment.

## Data Model
No data model changes. This is a configuration and DI-wiring change only.

## API / Interface Design

### Configuration shape (new)
```json
{
  "FileStorage": {
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

### Code surface (new)
```csharp
// backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageOptions.cs (or appropriate path)
public class FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public string BlobConnectionString { get; set; } = string.Empty;
}
```

```csharp
// FileStorageModule.cs
public static IServiceCollection AddFileStorageModule(this IServiceCollection services, IConfiguration configuration)
{
    services
        .AddOptions<FileStorageOptions>()
        .Bind(configuration.GetSection(FileStorageOptions.SectionName))
        .Validate(o => !string.IsNullOrWhiteSpace(o.BlobConnectionString),
                  "FileStorage:BlobConnectionString must be configured.")
        .ValidateOnStart();

    // ... existing service registrations, updated to consume IOptions<FileStorageOptions>
    return services;
}
```

### Key Vault secrets (new)
- Staging vault `kv-heblo-stg`: secret name `FileStorage--BlobConnectionString`.
- Production vault (name to be confirmed by deploying engineer, likely `kv-heblo-prod`): secret name `FileStorage--BlobConnectionString`.
- Provisioned via:
  ```
  az keyvault secret set --vault-name kv-heblo-stg \
    --name "FileStorage--BlobConnectionString" \
    --value "<staging-connection-string>"

  az keyvault secret set --vault-name <prod-vault-name> \
    --name "FileStorage--BlobConnectionString" \
    --value "<production-connection-string>"
  ```

## Dependencies
- Azure Key Vault: staging (`kv-heblo-stg`) and production vault.
- `Microsoft.Extensions.Options` and `Microsoft.Extensions.Options.ConfigurationExtensions` (already in the .NET 8 BCL / standard DI packages — no new NuGet dependency expected).
- The existing `FileStorage` and `ExpeditionList` modules.

## Out of Scope
- Any change to `ExpeditionList`'s use of `ExpeditionList:BlobConnectionString`. That key is in active production use by `AzureBlobPrintQueueSink` and stays exactly as-is.
- Removing the legacy `ExpeditionList:BlobConnectionString` Key Vault secret. It remains in place for ExpeditionList.
- Migrating any other module's configuration to follow this pattern, even if similar coupling exists elsewhere.
- Changing the underlying Azure Blob Storage account, container, or access pattern.
- Adding new tests beyond what FR-4 / NFR-4 require.

## Open Questions
None.

## Status: COMPLETE