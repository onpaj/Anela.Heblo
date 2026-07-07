# Design: Relocate IDocumentTextExtractor and IOneDriveService to Shared.Rag

## Component Design

This is a pure move/rename plus a DI-registration ownership change inside the
`Anela.Heblo.Application` assembly. No new components are introduced; existing components change
namespace, physical location, and (for one module) who registers them in DI. Interface shapes,
method signatures, and runtime behavior are unchanged.

### `Anela.Heblo.Application.Shared.Rag` (destination — existing namespace, new members)

Already hosts `SharedRagModule`, `WordWindowChunker`, `RagQueryExpander`, `RagFeatureOptions`,
`OneDriveFolderMapping`. Gains:

- **`IDocumentTextExtractor`** (`Shared/Rag/IDocumentTextExtractor.cs`) — unchanged contract:
  `bool CanHandle(string contentType)`, `Task<string> ExtractTextAsync(byte[] content,
  CancellationToken ct = default)`. Multiple implementations are resolved via
  `IEnumerable<IDocumentTextExtractor>` and selected by `CanHandle`.
- **`Anela.Heblo.Application.Shared.Rag.DocumentExtractors`** (`Shared/Rag/DocumentExtractors/`)
  — `PdfTextExtractor`, `WordDocumentExtractor`, `PlainTextExtractor`. Each implements
  `IDocumentTextExtractor` with identical internal logic to today; only namespace and file path
  change.
- **`IOneDriveService`** and the **`OneDriveFile`** record (`Shared/Rag/IOneDriveService.cs`) —
  unchanged contract: `ListInboxFilesAsync`, `DownloadFileAsync`, `MoveToArchivedAsync`,
  `DownloadFileTextByPathAsync`. `OneDriveFile` stays a `record` (internal Application-layer
  service type, never serialized over HTTP/OpenAPI — the project's "DTOs are classes" rule does
  not apply to it).
- **`Anela.Heblo.Application.Shared.Rag.OneDrive`** (`Shared/Rag/OneDrive/`) —
  - `GraphOneDriveService` — real Microsoft Graph implementation; unchanged dependencies
    (`ITokenAcquisition`, `IHttpClientFactory`, `IMemoryCache`, `GraphApiHelpers` from
    `Application.Common.Graph`, which is **not** moved).
  - `GraphFolderResolver` — stays `internal`; moves alongside `GraphOneDriveService` as its private
    implementation detail (folder-ID resolution/caching for Graph move operations). Assembly-level
    `internal` visibility is unaffected since old and new locations are both in
    `Anela.Heblo.Application`.
  - `MockOneDriveService` — no-auth substitute, used when SharePoint isn't configured or auth is
    mocked.
  - `GraphDriveModels` (renamed from the mis-filed `GraphApiHelpers.cs` in
    `KnowledgeBase/Services/`) — `internal` Graph JSON DTOs `GraphDriveItem`, `GraphFileFacet`,
    `GraphDriveItemCollection`, consumed by `GraphOneDriveService` and `GraphFolderResolver`. This
    file is a hard compile dependency of both and must move even though the original spec's FR-2
    list omitted it (per arch-review Specification Amendment 1). Renaming avoids continued
    collision with the real, unrelated `Application.Common.Graph.GraphApiHelpers`.
- **`SharedRagModule.AddSharedRagModule(IServiceCollection, IConfiguration)`** — signature change
  (previously parameterless). Registers `IWordWindowChunker`, `IRagQueryExpander` as before, plus
  newly: all three `IDocumentTextExtractor` implementations, and the Graph-vs-Mock
  `IOneDriveService` selection (including the conditional `AddHttpClient("MicrosoftGraph")` /
  `AddMemoryCache()` calls), moved verbatim from `KnowledgeBaseModule`. The selection logic
  continues to key off `configuration.GetSection("KnowledgeBase")` only (`KnowledgeBaseOptions`,
  `sharePointConfigured`, `UseMockAuth`, `BYPASS_JWT_VALIDATION`) — unchanged from today's behavior,
  not generalized to also consider `Leaflet:OneDriveFolderMappings` (that is a separate, pre-existing
  latent gap, explicitly out of scope for this refactor).

### `Anela.Heblo.Application.Features.KnowledgeBase.Services` (source — unaffected members stay)

Keeps `ChatTranscriptPreprocessor`, `IChunkSummarizer`/`ChunkSummarizer`,
`IConversationTopicSummarizer`/`ConversationTopicSummarizer`, `IIndexingStrategy` and its
implementations, and `IDocumentIndexingService`/`DocumentIndexingService` — these are genuinely
KnowledgeBase-owned and do not move. `DocumentIndexingService.cs` gains a new explicit
`using Anela.Heblo.Application.Shared.Rag;` (it currently has none, since it's declared inside the
namespace being vacated).

- **`KnowledgeBaseModule.AddKnowledgeBaseModule(IServiceCollection, IConfiguration)`** — drops the
  `IDocumentTextExtractor` x3 registrations, the `IOneDriveService` Graph/Mock block, the
  now-unused `AddHttpClient`/`AddMemoryCache` calls tied to it, and the local
  `kbOptions`/`sharePointConfigured`/`useMockAuth`/`bypassJwtValidation` variables. Keeps its own
  `services.AddOptions<KnowledgeBaseOptions>().Bind(...)` registration unchanged.

### `Anela.Heblo.Application.Features.Leaflet.*` (consumer — unchanged behavior, updated imports)

`UploadLeafletHandler`, `IndexLeafletHandler`, and `LeafletIngestionJob` resolve
`IDocumentTextExtractor`/`IOneDriveService` from `Shared.Rag` instead of
`Features.KnowledgeBase.Services`. After this change, Leaflet has zero compile-time reference to
any `Anela.Heblo.Application.Features.KnowledgeBase.*` namespace — closing out the four
`ModuleBoundariesTests.LeafletAllowlist` entries without introducing an adapter layer, since these
are generic infrastructure services, not KnowledgeBase business contracts.

### Composition root

`ApplicationModule.cs` changes its `AddSharedRagModule()` call (line ~61) to
`AddSharedRagModule(configuration)`, matching the `configuration`-parameterized signature already
used by every sibling `Add{X}Module(configuration)` call in the same file (`AddKnowledgeBaseModule`,
`AddLeafletModule`, `AddBankModule`, etc.).

### `ModuleBoundariesTests.cs`

`LeafletAllowlist` loses all four `IDocumentTextExtractor`/`IOneDriveService`/`OneDriveFile` entries
and their justification comment block, since the underlying namespace violation no longer exists.
The `"Leaflet -> KnowledgeBase"` theory case must be verified green with the allowlist emptied
before the entries are deleted, to isolate a genuine pass from an allowlist-editing mistake.

## Data Schemas

No database schema, migration, or persisted-entity changes. No public HTTP API/contract changes —
this refactor is entirely internal to the `Anela.Heblo.Application` assembly's compile-time
namespaces and DI wiring.

### `OneDriveFile` (unchanged shape, new namespace)

```csharp
namespace Anela.Heblo.Application.Shared.Rag;

public record OneDriveFile(string Id, string Name, string ContentType, string Path);
```

### `IDocumentTextExtractor` (unchanged shape, new namespace)

```csharp
namespace Anela.Heblo.Application.Shared.Rag;

public interface IDocumentTextExtractor
{
    bool CanHandle(string contentType);
    Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
}
```

### `IOneDriveService` (unchanged shape, new namespace)

```csharp
namespace Anela.Heblo.Application.Shared.Rag;

public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default);
    Task<string> MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default);
    Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct = default);
}
```

### Internal Graph DTOs (unchanged shape, new namespace + file rename)

`GraphDriveItem`, `GraphFileFacet`, `GraphDriveItemCollection` — `internal` JSON-deserialization
models used only by `GraphOneDriveService`/`GraphFolderResolver`, moving from the mis-named
`Features/KnowledgeBase/Services/GraphApiHelpers.cs` to
`Shared/Rag/OneDrive/GraphDriveModels.cs`, namespace `Anela.Heblo.Application.Shared.Rag.OneDrive`.
No field changes.

### DI registration signature change

```csharp
// Before
public static IServiceCollection AddSharedRagModule(this IServiceCollection services)

// After
public static IServiceCollection AddSharedRagModule(
    this IServiceCollection services,
    IConfiguration configuration)
```

Single call site (`ApplicationModule.cs`) updated accordingly; no other callers exist in
`backend/src` or `backend/test`.
