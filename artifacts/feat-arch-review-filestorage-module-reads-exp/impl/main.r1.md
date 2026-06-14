Now writing the output artifact.

---

# Implementation: Decouple FileStorage Module from ExpeditionList Configuration

## What was implemented

The `FileStorage` module no longer reads `configuration["ExpeditionList:BlobConnectionString"]`. It now owns its own `FileStorage:BlobConnectionString` configuration key, reads it via a strongly-typed `FileStorageOptions` class, and fails fast at startup in non-Development environments when the key is missing. The Development environment continues to fall back to `UseDevelopmentStorage=true` with a logged warning.

## Files created/modified

### New file
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs` — strongly-typed options class with `SectionName = "FileStorage"` and `BlobConnectionString` property (class, not record; [Required] intentionally omitted to allow Development fallback)

### Modified production code
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — new signature `(IServiceCollection, IConfiguration, IHostEnvironment)`; binds `FileStorageOptions` via `AddOptions<T>().Bind()`; env-branched `ValidateOnStart()` in non-Development; `BlobServiceClient` factory reads `IOptions<FileStorageOptions>` and falls back to `UseDevelopmentStorage=true` with warning in Development only
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — line 83: propagates `IHostEnvironment` to `AddFileStorageModule` (throws `InvalidOperationException` if null)

### Modified test file
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — updated all 6 existing tests to the new 3-arg signature; added 2 new tests covering FR-4 (fail-fast in Production with missing key, Development fallback with logged warning)

### Modified configuration
- `backend/src/Anela.Heblo.API/appsettings.json` — added `"FileStorage": { "BlobConnectionString": "<placeholder>" }` before `ExpeditionList`
- `backend/src/Anela.Heblo.API/appsettings.Development.json` — `UseDevelopmentStorage=true`
- `backend/src/Anela.Heblo.API/appsettings.Test.json` — `UseDevelopmentStorage=true` (Test env runs as non-Development; this ensures it can start without Key Vault)
- `backend/src/Anela.Heblo.API/appsettings.Staging.json` — empty string (Key Vault overlay provides real value via `FileStorage--BlobConnectionString`)
- `backend/src/Anela.Heblo.API/appsettings.Production.json` — empty string (Key Vault overlay)

### Modified docs
- `docs/architecture/environments.md` — appended `## 🔐 Module-owned Key Vault Secrets` table listing `FileStorage--BlobConnectionString` and `ExpeditionList--BlobConnectionString`, plus a rollout note dated 2026-06-12

## Tests

`backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — 8 tests total:
- 6 original tests (updated to new signature): singleton registration, instance identity, named HTTP client, no bare transient client, DownloadResilienceService singleton, constant export
- `AddFileStorageModule_NonDevelopmentEnvironmentWithMissingKey_FailsValidation` — asserts `OptionsValidationException` thrown with message containing `FileStorage:BlobConnectionString`
- `AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning` — asserts `BlobServiceClient` resolves (no throw) and `ILogger<AzureBlobStorageService>` received `LogWarning` once containing `FileStorage:BlobConnectionString`

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-filestorage-module-reads-exp

# 1. All FileStorage tests (8) + CombinedPrintQueueSinkRegistrationTests (4) must pass
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~FileStorageModuleTests|FullyQualifiedName~CombinedPrintQueueSinkRegistrationTests"

# 2. FR-6 greps — all must be empty / contain only expected files
git grep -n 'ExpeditionList:' -- 'backend/src/Anela.Heblo.Application/Features/FileStorage/**'
git grep -n 'configuration\["FileStorage:' -- backend/
git grep -n 'ExpeditionList:BlobConnectionString' -- backend/

# 3. Full build
dotnet build
```

## Notes

**Pre-merge operational prerequisites (out-of-code — must be done by deploying engineer before merge):**
- Confirm production Key Vault name (likely `kv-heblo-prod`) via `az keyvault list`
- Provision `FileStorage--BlobConnectionString` in `kv-heblo-stg` and production vault using the same connection string currently in `ExpeditionList--BlobConnectionString`
- Verify both secrets exist before merge (NFR-3 "secret first")
- 38 pre-existing test failures in the full suite are Docker-container-related and unrelated to this change

**FR-6 grep results from verification run:**
- `ExpeditionList:` in FileStorage source/tests → 0 matches ✓
- `ExpeditionList:BlobConnectionString` in backend/ → 1 match only (`CombinedPrintQueueSinkRegistrationTests.cs:27`) ✓
- `configuration["FileStorage:` in backend/ → 0 matches ✓
- `"FileStorage"` in backend/ → 6 matches (FileStorageOptions.cs × 1 + 5 appsettings files) ✓

## PR Summary

Decouples `FileStorageModule` from the `ExpeditionList` configuration namespace. The module previously read `configuration["ExpeditionList:BlobConnectionString"]`, a hidden cross-module coupling flagged by the arch-review routine on 2026-06-05. The fix introduces a `FileStorageOptions` class, binds it via the standard options pattern, and adds fail-fast startup validation in non-Development environments so a missing `FileStorage:BlobConnectionString` key surfaces as an `OptionsValidationException` at boot rather than silently routing writes to the storage emulator in production.

**IMPORTANT: Key Vault secrets must be provisioned in staging (`kv-heblo-stg`) and production before this PR is merged.** The production vault name must be confirmed and recorded here. See the "Module-owned Key Vault Secrets" table in `docs/architecture/environments.md` for the exact secret names.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageOptions.cs` — new strongly-typed options class (class, not record per project convention)
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — options binding, env-aware ValidateOnStart, IOptions-backed BlobServiceClient factory
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — propagates IHostEnvironment to AddFileStorageModule
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — updated to new signature + 2 new FR-4 tests (8 total, all pass)
- `backend/src/Anela.Heblo.API/appsettings*.json` — 5 files: added `FileStorage:BlobConnectionString` section to appsettings.json, .Development.json, .Test.json, .Staging.json, .Production.json
- `docs/architecture/environments.md` — module-owned Key Vault secrets table + rollout note

## Status

DONE