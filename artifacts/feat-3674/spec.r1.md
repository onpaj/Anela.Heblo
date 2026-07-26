# Specification: Remove Azure Adapter's Compile-Time Dependency on the Application Layer (FileStorage)

## Summary
`AzureBlobStorageService` — the Azure Blob Storage implementation of `IBlobStorageService` living in the Adapters (infrastructure) ring — currently imports `Anela.Heblo.Application.Features.FileStorage.FileStorageModule` solely to read the constant string `FileDownloadClientName` ("FileDownload") used to resolve a named `HttpClient`. This violates the Clean Architecture dependency rule (adapters must depend inward on Domain, never on Application) and creates an avoidable coupling: any refactor of `FileStorageModule` can break the adapter for reasons unrelated to blob storage. This spec moves the constant to the Domain layer so the adapter depends only on Domain types, while leaving the adapter's existing (and separately justified) `ProjectReference` to the Application project and `AzureAdapterModule.cs`'s use of Application-layer Options classes untouched.

## Background
`AzureBlobStorageService.cs` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`) implements `IBlobStorageService`, which is correctly defined in the Domain layer (`Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService`). Inside `DownloadFromUrlAsync` (line 39), the adapter calls:

```csharp
var httpClient = _httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName);
```

`FileStorageModule` (`backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`) is an `IServiceCollection` extension class in the Application layer. It both defines `public const string FileDownloadClientName = "FileDownload";` and registers the matching named `HttpClient` via `services.AddHttpClient(FileDownloadClientName)...`. The Application-layer `DownloadFromUrlHandler` also references the same constant (both to pass it through to a resilience helper and to issue a HEAD probe).

This is a narrowly-scoped fix, not a wholesale re-architecture of the Azure adapter project:
- `Anela.Heblo.Adapters.Azure.csproj` has exactly one `ProjectReference`, to `Anela.Heblo.Application.csproj`. It does **not** have a direct reference to `Anela.Heblo.Domain.csproj` today — `IBlobStorageService` and other Domain types are visible to the adapter only *transitively*, because `Anela.Heblo.Application.csproj` itself references `Anela.Heblo.Domain.csproj`.
- `AzureAdapterModule.cs` (the adapter's composition-root/DI-wiring class, in the same project) legitimately reads Application-layer Options classes today (`FileStorageOptions`, `PrintPickingListOptions`) to construct `BlobServiceClient` and `BlobContainerClient` instances. This composition-root pattern — where an adapter's own `IServiceCollection` extension method reaches into Application for strongly-typed configuration — is an established, accepted pattern elsewhere in this codebase (see e.g. `Anela.Heblo.Adapters.WebSearch`, `Anela.Heblo.Adapters.Microsoft365`, `Anela.Heblo.Adapters.Plaud`, all of which import `Anela.Heblo.Application.*` in their own `*AdapterServiceCollectionExtensions.cs`/module files). **This spec does not change that pattern and does not remove the adapter project's `ProjectReference` to Application.**
- What is out of line with the rest of the codebase is a concrete *service implementation* (`AzureBlobStorageService`, not a DI-wiring/module class) reaching into Application for a plain string constant used in ordinary business logic. That is the specific defect this spec fixes.

The codebase already has precedent for Domain-layer constant classes, e.g. `Anela.Heblo.Domain.Features.Purchase.PurchaseOrderConstants`, which holds `public const string` validation-message constants. This spec follows that established pattern rather than introducing a new options-injection mechanism, because the value in question is a fixed logical `HttpClient` name (not environment-specific configuration) shared as a compile-time identifier between the Application-layer registration (`services.AddHttpClient(name)`) and the Domain-typed adapter that resolves it (`IHttpClientFactory.CreateClient(name)`).

## Functional Requirements

### FR-1: Add a Domain-layer constant for the file-download `HttpClient` name
Add a new type `FileStorageConstants` in namespace `Anela.Heblo.Domain.Features.FileStorage` (new file `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs`, alongside the existing `IBlobStorageService.cs` in that folder), exposing:

```csharp
namespace Anela.Heblo.Domain.Features.FileStorage;

public static class FileStorageConstants
{
    /// <summary>
    /// Logical name of the HttpClient (registered via IHttpClientFactory) used for
    /// downloading files from external URLs before uploading them to blob storage.
    /// </summary>
    public const string FileDownloadClientName = "FileDownload";
}
```

**Acceptance criteria:**
- `Anela.Heblo.Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName` exists, is `public const string`, and equals `"FileDownload"` (unchanged value — no behavioral/runtime change).
- The new file lives under `backend/src/Anela.Heblo.Domain/Features/FileStorage/`, matching the existing placement of `IBlobStorageService.cs`.
- No new `ProjectReference` is required for `Anela.Heblo.Domain.csproj` itself (it has none today for this feature and needs none).

### FR-2: Adapter consumes the Domain constant instead of the Application module
Update `AzureBlobStorageService.cs` so it no longer references `Anela.Heblo.Application.Features.FileStorage.FileStorageModule` anywhere:
- Remove `using Anela.Heblo.Application.Features.FileStorage;`.
- Change line 39 to `var httpClient = _httpClientFactory.CreateClient(FileStorageConstants.FileDownloadClientName);`, relying on the existing `using Anela.Heblo.Domain.Features.FileStorage;` already present in the file.

**Acceptance criteria:**
- `AzureBlobStorageService.cs` contains no `using` directive and no fully-qualified reference to any `Anela.Heblo.Application.*` namespace or type.
- `git grep -n "Anela.Heblo.Application" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` returns no matches.
- The named `HttpClient` resolved by the adapter (`"FileDownload"`) is unchanged, so it still matches the client registered by `FileStorageModule.AddFileStorageModule` at startup — no behavioral regression.

### FR-3: Preserve backward compatibility for existing Application-layer consumers
`FileStorageModule.FileDownloadClientName` is also referenced today by `DownloadFromUrlHandler.cs` (Application layer, two call sites) and by several existing unit tests. These are Application-layer-to-Application-layer references and are architecturally fine as-is; they are not required to change. To avoid a duplicated magic string (one source of truth) while keeping every existing call site compiling unchanged, `FileStorageModule` keeps a `FileDownloadClientName` member but its value is sourced from the new Domain constant:

```csharp
public const string FileDownloadClientName = FileStorageConstants.FileDownloadClientName;
```

(A `const` may reference another `const` at compile time in C#, so this remains a compile-time constant — no behavioral change to `services.AddHttpClient(FileDownloadClientName)` in the same file.)

**Acceptance criteria:**
- `FileStorageModule.FileDownloadClientName` still exists, is still `public const string`, and still equals `"FileDownload"`.
- `DownloadFromUrlHandler.cs` requires no code change and continues to compile and behave identically.
- Existing test `FileStorageModuleTests.cs` line 102 (`Assert.Equal("FileDownload", FileStorageModule.FileDownloadClientName);`) continues to pass unmodified.

### FR-4: Align the adapter's unit tests with its corrected dependency direction
`AzureBlobStorageServiceTests.cs` (`backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`) currently imports `Anela.Heblo.Application.Features.FileStorage` and uses `FileStorageModule.FileDownloadClientName` in ten `Mock<IHttpClientFactory>` setup/verify calls (lines 31, 75, 87, 146, 171, 424, 453, 481, 507, 547, 574). Since the test project already has project references to every layer, this is not a compile-time architecture violation — but leaving it as-is would mean the test for an "adapter now depends only on Domain" fix still visibly depends on Application, which is confusing and undercuts the intent of the change. Update these references to `Anela.Heblo.Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName`, replacing the `using Anela.Heblo.Application.Features.FileStorage;` import with `using Anela.Heblo.Domain.Features.FileStorage;` (the latter is likely already needed/present given the test constructs `AzureBlobStorageService`).

**Acceptance criteria:**
- `AzureBlobStorageServiceTests.cs` contains no reference to `Anela.Heblo.Application.Features.FileStorage.FileStorageModule`.
- All existing tests in `AzureBlobStorageServiceTests.cs` continue to pass unmodified in behavior (only the constant's source changes, not its value).

### FR-5: No changes to DI registration, HttpClient behavior, or public contracts
This is a pure internal refactor of where a constant is declared. No changes are required or permitted to:
- `AzureAdapterModule.cs` (DI wiring stays as-is; its existing references to `Anela.Heblo.Application.Features.FileStorage` for `FileStorageOptions` are out of scope — see Background).
- `FileStorageModule.AddFileStorageModule`'s `services.AddHttpClient(FileDownloadClientName)...` registration logic (socket pooling, timeout, decompression settings) — unchanged.
- `IBlobStorageService`'s public interface — unchanged.
- `Anela.Heblo.Adapters.Azure.csproj`'s `ProjectReference` to `Anela.Heblo.Application.csproj` — stays, because `AzureAdapterModule.cs` still needs it (see Background).

**Acceptance criteria:**
- `AzureAdapterModule.cs` diff is empty.
- `FileStorageModule.AddFileStorageModule` method body is unchanged except for FR-3's one-line constant declaration.
- `Anela.Heblo.Adapters.Azure.csproj` retains its `ProjectReference` to `Anela.Heblo.Application.csproj` (removing it would break `AzureAdapterModule.cs`'s existing, in-scope-elsewhere use of `FileStorageOptions`/`PrintPickingListOptions`).

## Non-Functional Requirements

### NFR-1: Build correctness
`dotnet build` (full solution) and `dotnet format` must succeed with zero new errors or warnings after the change, per this repository's standard validation gate.

### NFR-2: No runtime/behavioral change
This is a compile-time-only refactor. The named `HttpClient` string value (`"FileDownload"`), its registration (pooled sockets, infinite `HttpClient.Timeout`, automatic decompression), and every consumer's runtime behavior must be byte-for-byte identical before and after. No new configuration keys, no new appsettings entries, no Key Vault changes.

### NFR-3: Test coverage preserved
All existing tests touching this area must pass unmodified in assertions (only import/reference changes per FR-4):
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/SimpleFileStorageTest.cs`

### NFR-4: Architectural boundary verification
Add (or run manually and record in the PR) a repo-wide grep to prove the fix:
```bash
git grep -n "Anela.Heblo.Application" -- backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs
```
Expected: empty output. This should be recorded as evidence in the PR description; a permanent architecture test (e.g. NetArchTest/dependency-graph assertion) is explicitly out of scope for this change (see Out of Scope).

## Data Model
Not applicable — this change introduces no new persisted or transmitted data structures. `FileStorageConstants` is a compile-time constant holder, not a domain entity.

## API / Interface Design
Not applicable — no public API surface (HTTP endpoints, MediatR requests, DTOs) changes. The only "interface" affected is the internal C# namespace/type from which `AzureBlobStorageService` sources one string constant:

| Before | After |
|---|---|
| `Anela.Heblo.Application.Features.FileStorage.FileStorageModule.FileDownloadClientName` | `Anela.Heblo.Domain.Features.FileStorage.FileStorageConstants.FileDownloadClientName` |

`Anela.Heblo.Application.Features.FileStorage.FileStorageModule.FileDownloadClientName` continues to exist (FR-3) as a forwarding const for existing Application-layer consumers.

## Dependencies
- No new NuGet packages.
- No new `ProjectReference` entries required (Domain has no dependents to add; Adapter already sees Domain types transitively via its existing Application reference and already has a `using Anela.Heblo.Domain.Features.FileStorage;` directive in the target file for `IBlobStorageService`/`BlobItemInfo`).
- Depends on the existing `PurchaseOrderConstants` precedent in `Anela.Heblo.Domain.Features.Purchase` only as a style reference, not a code dependency.

## Out of Scope
- Removing `Anela.Heblo.Adapters.Azure.csproj`'s `ProjectReference` to `Anela.Heblo.Application.csproj` entirely. `AzureAdapterModule.cs` still legitimately needs `FileStorageOptions` and `PrintPickingListOptions` from Application for its `IServiceCollection` wiring — a pattern used consistently by other adapters in this codebase (WebSearch, Microsoft365, Plaud, OrgChart, etc.). Changing that broader pattern is a separate, much larger architectural discussion and not part of this fix.
- Adding a permanent automated architecture/dependency test (e.g., NetArchTest) that fails the build if any Adapter service implementation imports `Anela.Heblo.Application.*`. This spec only fixes the one known violation; introducing enforcement tooling is a separate initiative the team may pursue later.
- Moving `FileStorageOptions`, `FileDownloadOptions`, or any other Application-layer FileStorage configuration classes to Domain. Only the single `FileDownloadClientName` string constant moves.
- Renaming the `"FileDownload"` HttpClient logical name itself.
- Any change to `DownloadFromUrlHandler.cs` — it stays on `FileStorageModule.FileDownloadClientName` (Application-to-Application reference, architecturally valid).
- Auditing or fixing the other ~39 files across the codebase found to `using Anela.Heblo.Application.*` from Adapter projects (per repository grep during investigation) — most of those are legitimate DI-wiring/module files (`*AdapterServiceCollectionExtensions.cs`), not service implementations reaching into Application for business-logic constants. A broader audit is future work, not part of this fix.

## Open Questions
None.

## Status: COMPLETE
