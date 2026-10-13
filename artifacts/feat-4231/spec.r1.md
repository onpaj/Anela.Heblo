# Specification: Decouple ExpeditionListArchive from FileStorage's IBlobStorageService

## Summary
`ExpeditionListArchive`'s four handlers inject the FileStorage module's full `IBlobStorageService` (7 methods) directly and reference its `BlobItemInfo` domain type, violating the documented cross-module communication rule (consumer defines a narrow contract; provider adapts to it). This spec defines a narrow `IExpeditionListArchiveBlobStore` contract owned by `ExpeditionListArchive`, a `FileStorage`-side adapter implementing it, and the DI/handler changes to swap the four handlers onto the new contract — with zero behavioral change.

## Background
This finding was filed by the daily arch-review routine (issue #4231, 2026-09-20). The codebase already has an established pattern for this exact class of problem — `ILeafletKnowledgeSource` (Leaflet-owned contract) implemented by `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned adapter), registered in `KnowledgeBaseModule`, and enforced by a reflection-based test in `ModuleBoundariesTests.cs`. Several other module pairs (ExpeditionList → ShoptetOrders, ShoptetOrders → ShipmentLabels, DataQuality → Catalog, etc.) follow the identical shape. This spec applies that same pattern to `ExpeditionListArchive → FileStorage`.

Verified current state (read directly from the branch):
- `IBlobStorageService` (`backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`) exposes 7 members: `DownloadFromUrlAsync`, `UploadAsync`, `DeleteAsync`, `GetBlobUrl`, `ExistsAsync`, `ListBlobsAsync`, `DownloadAsync`, `ListVirtualDirectoriesAsync`.
- The four `ExpeditionListArchive` handlers use only 3 of these: `DownloadAsync` (used by `DownloadExpeditionListHandler` and `ReprintExpeditionListHandler`), `ListBlobsAsync` (used by `GetExpeditionListsByDateHandler`, which also reads `BlobItemInfo.Name`, `.FileName`, `.CreatedOn`, `.ContentLength`), and `ListVirtualDirectoriesAsync` (used by `GetExpeditionDatesHandler`).
- `IBlobStorageService` is registered as a `Singleton` in `AzureAdapterModule.AddAzureBlobStorageService` (`backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs`), implemented by `AzureBlobStorageService`.
- `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs`) contains a **manual factory override** for `ReprintExpeditionListHandler`'s registration (to pick the keyed `"cups"` `IPrintQueueSink` when present) that itself resolves `IBlobStorageService` from the container — this factory must be updated alongside the handler.
- `ModuleBoundariesTests.cs` (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) already has an `ExpeditionListArchive -> ExpeditionList` rule but **no** `ExpeditionListArchive -> FileStorage` rule yet — this is why the current violation compiles and passes CI undetected.
- Existing unit tests for the four handlers (`backend/test/Anela.Heblo.Tests/ExpeditionListArchive/*.cs`) construct `Mock<IBlobStorageService>` directly and pass `.Object` into the handler constructors; these must be updated to mock the new contract instead.

## Functional Requirements

### FR-1: Define the narrow consumer-owned contract
Create `IExpeditionListArchiveBlobStore` in `Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs`, exposing exactly the three operations the module uses today, with signatures shaped to match current call sites:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public interface IExpeditionListArchiveBlobStore
{
    Task<Stream> DownloadAsync(string containerName, string blobPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExpeditionBlobItem>> ListBlobsAsync(string containerName, string? prefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListVirtualDirectoriesAsync(string containerName, CancellationToken cancellationToken);
}
```

**Acceptance criteria:**
- The interface lives under `Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts` and contains no reference to `Anela.Heblo.Domain.Features.FileStorage` types.
- It exposes only the 3 members listed above — no speculative extra members (e.g. no `UploadAsync`, `DeleteAsync`, `ExistsAsync`, `GetBlobUrl`, `DownloadFromUrlAsync`).
- Method names/parameter shapes are picked so that call sites need no logic changes beyond the type/DI swap (only rename `blobName` param usage to match existing call-site argument order: `containerName`, then path/prefix, then `cancellationToken`).

### FR-2: Introduce a consumer-owned DTO replacing `BlobItemInfo`
Create `ExpeditionBlobItem` in the same `Contracts/` folder, replacing `Anela.Heblo.Domain.Features.FileStorage.BlobItemInfo` at the `ExpeditionListArchive` boundary:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

public class ExpeditionBlobItem
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset? CreatedOn { get; set; }
    public long? ContentLength { get; set; }
}
```

**Acceptance criteria:**
- `ExpeditionBlobItem` is a plain class (not a record — matches this repo's DTO convention) with the same 4 members `GetExpeditionListsByDateHandler` currently reads off `BlobItemInfo` (`Name`, `FileName`, `CreatedOn`, `ContentLength`), same types.
- `ExpeditionListArchive` production code no longer references `BlobItemInfo` anywhere.

### FR-3: Provide a FileStorage-side adapter implementing the contract
Add an adapter class implementing `IExpeditionListArchiveBlobStore` by delegating to the existing `IBlobStorageService`, mapping `BlobItemInfo` → `ExpeditionBlobItem`.

**Acceptance criteria:**
- The adapter is named `ExpeditionListArchiveBlobStoreAdapter` (or equivalent) and lives in a `FileStorage`-owned `Infrastructure/` location — the architect phase must decide the exact home (see Open Questions: Azure Adapters project vs. `Anela.Heblo.Application.Features.FileStorage.Infrastructure`) and record the decision in `arch-review.r1.md`, following the same placement logic used for `KnowledgeBaseLeafletSourceAdapter` (provider-owned, in the provider's `Infrastructure/` namespace).
- The adapter's constructor takes `IBlobStorageService` and nothing else.
- `DownloadAsync` delegates 1:1 to `IBlobStorageService.DownloadAsync`.
- `ListBlobsAsync` delegates to `IBlobStorageService.ListBlobsAsync` and maps each returned `BlobItemInfo` to `ExpeditionBlobItem` field-for-field.
- `ListVirtualDirectoriesAsync` delegates 1:1 to `IBlobStorageService.ListVirtualDirectoriesAsync`.
- No business logic (filtering, sorting, validation) is added in the adapter — it is a pure mapping/delegation layer, matching `KnowledgeBaseLeafletSourceAdapter`'s shape.

### FR-4: Register the DI binding
Register `services.AddScoped<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>()` (or `AddSingleton`, matching `IBlobStorageService`'s own current Singleton lifetime, to avoid a lifetime-mismatch captive-dependency issue) in whichever module owns the adapter per FR-3's placement decision — mirroring how `KnowledgeBaseModule.AddKnowledgeBaseModule` registers `ILeafletKnowledgeSource`'s binding, never in `ExpeditionListArchiveModule.cs`.

**Acceptance criteria:**
- The binding is registered exactly once, in the provider (FileStorage-side) module registration method, not in `ExpeditionListArchiveModule.cs`.
- `ExpeditionListArchiveModule.cs` retains only its own module's registrations (options binding, the `ReprintExpeditionListHandler` custom factory) and is updated to resolve `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService` inside that factory (see FR-5).
- App composition root (`Program.cs`) already calls both `AddAzureBlobStorageService` and `AddExpeditionListArchiveModule`; verify (do not need to reorder, since DI resolution — not registration order — determines availability) that the new binding's registration call is added wherever the FileStorage/Azure adapter module is composed, so it resolves correctly at runtime.

### FR-5: Migrate all four handlers off `IBlobStorageService`
Replace the constructor-injected `IBlobStorageService _blobStorageService` field with `IExpeditionListArchiveBlobStore` in all four handlers, updating the `using Anela.Heblo.Domain.Features.FileStorage;` import to `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;` (already present via existing DTO usage in 2 of the 4 files) and removing the FileStorage `using` entirely:

- `DownloadExpeditionListHandler` — `DownloadAsync` call swapped to the new interface, same signature/args.
- `ReprintExpeditionListHandler` — `DownloadAsync` call swapped; **also update the manual factory in `ExpeditionListArchiveModule.cs`** that currently does `provider.GetRequiredService<IBlobStorageService>()` to resolve `IExpeditionListArchiveBlobStore` instead.
- `GetExpeditionListsByDateHandler` — `ListBlobsAsync` call swapped; the `.Select(b => new ExpeditionListItemDto { ... })` projection now maps from `ExpeditionBlobItem` instead of `BlobItemInfo` (property names are identical, so the LINQ body itself needs no change beyond the parameter's inferred type).
- `GetExpeditionDatesHandler` — `ListVirtualDirectoriesAsync` call swapped, same signature/args.

**Acceptance criteria:**
- None of the four handler files contain `using Anela.Heblo.Domain.Features.FileStorage;` after the change.
- None of the four handler files reference `IBlobStorageService` or `BlobItemInfo` after the change.
- Handler `Handle()` method bodies are otherwise byte-for-byte unchanged (validation logic, filtering, pagination, error codes all stay exactly as they are today) — this is a pure dependency-substitution refactor, not a behavior change.
- `dotnet build` succeeds with zero new warnings.

### FR-6: Update existing unit tests
Update the 4 existing handler test files under `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/` to construct `Mock<IExpeditionListArchiveBlobStore>` instead of `Mock<IBlobStorageService>`, and to build `ExpeditionBlobItem` instances instead of `BlobItemInfo` instances in test fixtures (`GetExpeditionListsByDateHandlerTests.cs`).

**Acceptance criteria:**
- All 4 existing test files compile and pass against the new contract with **no assertion changes** (same test names, same expected values) — only the mocked type and constructed DTO type change.
- `DownloadFromUrlHandlerTests.cs` and other `FileStorage`-module tests (which legitimately mock `IBlobStorageService` for FileStorage's own handlers) are untouched — this refactor does not touch `IBlobStorageService` itself or any of its other consumers.

### FR-7: Add/extend the module-boundary guard test
Add a new `ModuleBoundaryRule` entry to `ModuleBoundariesTests.cs`'s `Rules()` `TheoryData`, named `"ExpeditionListArchive -> FileStorage"`, with:
- `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionListArchive"`
- `ForbiddenNamespacePrefixes: ["Anela.Heblo.Domain.Features.FileStorage", "Anela.Heblo.Application.Features.FileStorage", "Anela.Heblo.Persistence.FileStorage"]` (matching the 3-prefix shape used by every other rule)
- `Allowlist: new HashSet<string>(StringComparer.Ordinal)` — empty, since after this refactor `ExpeditionListArchive` should have zero references into `FileStorage`.

**Acceptance criteria:**
- The new rule is added to the existing `Rules()` `TheoryData` (not a new test method) — consistent with how every other module pair is enforced.
- Running the full `ModuleBoundariesTests` theory (including the new rule) passes with zero violations once FR-1–FR-6 are complete.
- This test is the regression guard: any future PR that reintroduces a direct `ExpeditionListArchive → FileStorage` reference will fail CI, exactly as the `ILeafletKnowledgeSource` example already prevents Leaflet → KnowledgeBase regressions.

## Non-Functional Requirements

### NFR-1: Zero behavioral change
This is a pure refactor: the four handlers' inputs, outputs, error codes, and side effects (blob download, print-queue submission, listing/pagination) must be identical before and after. No new validation, no new error paths, no changed response DTOs.

### NFR-2: No API/contract surface change
`DownloadExpeditionListRequest/Response`, `ReprintExpeditionListRequest/Response`, `GetExpeditionListsByDateRequest/Response`, `GetExpeditionDatesRequest/Response`, and `ExpeditionListItemDto` are all unchanged — this refactor is entirely internal to the handlers' dependency wiring.

### NFR-3: DI lifetime correctness
The new `IExpeditionListArchiveBlobStore` binding's lifetime must not create a captive-dependency problem: since `IBlobStorageService` is registered `Singleton`, the adapter wrapping it should also be `Singleton` (or, if `Scoped`, the architect must confirm no scoped/transient state is introduced that would break under a wider Singleton consumer — currently none of the four handlers are Singleton themselves, so `Scoped` is also safe, but `Singleton` is simpler and consistent with the wrapped dependency).

## Data Model
No persistent data model changes. Two new in-memory-only contract types are introduced:
- `IExpeditionListArchiveBlobStore` (interface, `Contracts/`)
- `ExpeditionBlobItem` (DTO class, `Contracts/`) — structurally identical to `BlobItemInfo` but owned by `ExpeditionListArchive`.

## API / Interface Design
No public HTTP API changes. Internal interface changes only:
- New: `IExpeditionListArchiveBlobStore.DownloadAsync(string containerName, string blobPath, CancellationToken)`
- New: `IExpeditionListArchiveBlobStore.ListBlobsAsync(string containerName, string? prefix, CancellationToken)`
- New: `IExpeditionListArchiveBlobStore.ListVirtualDirectoriesAsync(string containerName, CancellationToken)`
- New: `ExpeditionListArchiveBlobStoreAdapter` implementing the above, wrapping `IBlobStorageService`.
- Removed (from the four handlers' constructors only — the interface itself is untouched and still used elsewhere): direct `IBlobStorageService` injection.

## Dependencies
- Depends on the existing `IBlobStorageService` / `AzureBlobStorageService` / `BlobItemInfo` (`FileStorage` module) remaining unchanged — this refactor adapts to that interface, it does not modify it.
- Depends on the existing `ExpeditionListArchiveOptions.BlobContainerName` configuration remaining as-is (handlers keep reading it via `IOptions<ExpeditionListArchiveOptions>`, unchanged).
- No new NuGet packages or external service dependencies.

## Out of Scope
- Any change to `IBlobStorageService` itself or its other consumers (`DownloadFromUrlHandler` in the `FileStorage` module, print-queue sinks, etc.).
- Any change to `ExpeditionListArchive`'s existing (and already-allowlisted) `ExpeditionList` module boundary rule.
- Any behavioral, validation, or API surface change to the four use cases.
- Broader FileStorage module restructuring beyond adding the one new adapter class and its DI registration.

## Open Questions
- **Adapter placement**: should `ExpeditionListArchiveBlobStoreAdapter` live in `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/` (next to `AzureBlobStorageService`) or in a new `Anela.Heblo.Application.Features.FileStorage.Infrastructure` namespace (mirroring `KnowledgeBaseLeafletSourceAdapter`'s placement pattern, which lives in the Application layer, not the Adapters project)? The issue body's suggested fix says "in the Azure Adapters project or FileStorage module" — leaving both options open. **Assumption for this spec**: the canonical example (`KnowledgeBaseLeafletSourceAdapter`) places the adapter in the Application-layer provider module's `Infrastructure/` folder, so this spec defaults to `Anela.Heblo.Application.Features.FileStorage.Infrastructure.ExpeditionListArchiveBlobStoreAdapter`, registered in FileStorage's own module registration method (there does not appear to be a `FileStorageModule.cs` today — confirm during architecture review whether one exists or whether registration should go in `AzureAdapterModule.cs` instead, since that is where `IBlobStorageService` itself is currently bound). The architect phase must confirm or override this placement.
- **DI lifetime**: confirmed as `Singleton` per NFR-3, but the architect should double check no hidden scoped dependency creeps in via the adapter.

## Status: HAS_QUESTIONS
