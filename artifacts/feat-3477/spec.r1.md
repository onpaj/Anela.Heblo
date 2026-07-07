# Specification: Relocate IDocumentTextExtractor and IOneDriveService to a shared namespace

## Summary
Move the cross-cutting document-ingestion services `IDocumentTextExtractor` (with its three
implementations) and `IOneDriveService`/`OneDriveFile` (with its two implementations) out of
`Anela.Heblo.Application.Features.KnowledgeBase.Services` and into the existing shared RAG
namespace family (`Anela.Heblo.Application.Shared.Rag`), so that the Leaflet module no longer
takes a compile-time dependency on the KnowledgeBase module. This removes the four pre-existing
entries in `LeafletAllowlist` in `ModuleBoundariesTests.cs` and closes out the tracking item
called for in that allowlist's comment (added 2026-05-15).

## Background
`ModuleBoundariesTests.cs` enforces that Leaflet must not directly reference KnowledgeBase-owned
types; cross-module contracts should be consumer-owned interfaces (e.g. `ILeafletKnowledgeSource`)
implemented by the provider via an adapter — this is exactly how `ILeafletKnowledgeSource` /
`KnowledgeBaseLeafletSourceAdapter` already works between these two modules.

`IDocumentTextExtractor` and `IOneDriveService` don't fit that adapter pattern, however: they are
not KnowledgeBase business contracts, they are generic infrastructure services (file-format text
extraction, OneDrive/SharePoint file access) that both KnowledgeBase and Leaflet use directly and
identically for their own document-ingestion pipelines. Wrapping them in per-module adapters would
just add indirection without adding meaning. The codebase already has a precedent for this
situation: `Anela.Heblo.Application.Shared.Rag` (`SharedRagModule`, `WordWindowChunker`,
`RagQueryExpander`) and `Anela.Heblo.Domain.Shared.Rag` (`DocumentType`, `OneDriveFolderMapping`)
already host RAG-related types shared across modules, and `LeafletIngestionJob.cs` already imports
`Anela.Heblo.Domain.Shared.Rag`. Relocating the two remaining KnowledgeBase-owned service
abstractions into the same shared family is the natural fix, and it is the fix the allowlist
comment itself calls for ("remove these entries when `IDocumentTextExtractor` is relocated to a
shared namespace").

Today the allowlist entries let the boundary test pass despite the violation; this creates silent
risk — if KnowledgeBase is ever split out or its internals reshuffled, Leaflet (and its background
ingestion job) can break with no compile-time signal, only a boundary-test failure that a developer
might be tempted to "fix" by adding yet another allowlist entry instead of addressing the root
cause.

## Functional Requirements

### FR-1: Relocate `IDocumentTextExtractor` and its implementations
Move the following types out of `Anela.Heblo.Application.Features.KnowledgeBase.Services` /
`...Services.DocumentExtractors` into a new shared namespace,
`Anela.Heblo.Application.Shared.Rag`:

- `IDocumentTextExtractor` (currently `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs`)
- `PdfTextExtractor` (currently `.../Services/DocumentExtractors/PdfTextExtractor.cs`)
- `WordDocumentExtractor` (currently `.../Services/DocumentExtractors/WordDocumentExtractor.cs`)
- `PlainTextExtractor` (currently `.../Services/DocumentExtractors/PlainTextExtractor.cs`)

Target physical location: `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/`
(interface directly under `Shared/Rag/`, implementations under a `DocumentExtractors/`
subfolder, mirroring the current KnowledgeBase layout), all under namespace
`Anela.Heblo.Application.Shared.Rag` (implementations may use a `.DocumentExtractors` sub-namespace
if the team prefers to mirror the current split — either is acceptable as long as it is not
`Features.KnowledgeBase.*`).

**Acceptance criteria:**
- `IDocumentTextExtractor` and its three implementations no longer live under any
  `Anela.Heblo.Application.Features.KnowledgeBase.*` namespace.
- All existing behavior of `PdfTextExtractor`, `WordDocumentExtractor`, and `PlainTextExtractor`
  (content-type matching via `CanHandle`, text extraction via `ExtractTextAsync`) is preserved
  unchanged — this is a pure move/rename, not a behavioral change.
- All existing unit tests for these types (`PdfTextExtractorTests`, `WordDocumentExtractorTests`,
  `PlainTextExtractorTests` under `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/`) are
  updated to the new namespace and continue to pass. Test file location may stay as-is or move to
  mirror the new namespace (`Shared/Rag/...`); either is acceptable, but must be consistent with
  whatever test-layout convention the team already applies to other `Shared` types (see
  Open Questions).

### FR-2: Relocate `IOneDriveService`, `OneDriveFile`, and its implementations
Move the following types out of `Anela.Heblo.Application.Features.KnowledgeBase.Services` into
`Anela.Heblo.Application.Shared.Rag`:

- `IOneDriveService` and the `OneDriveFile` record (currently both declared in
  `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs`)
- `GraphOneDriveService` (currently `.../Services/GraphOneDriveService.cs`) — real Microsoft Graph
  implementation, depends on `ITokenAcquisition`, `IHttpClientFactory`, `IMemoryCache`, and the
  existing `Anela.Heblo.Application.Common.Graph.GraphApiHelpers` helper (which stays where it is;
  it's already a shared `Common` utility used by other modules such as `MeetingTasks` and
  `CatalogDocuments`).
- `MockOneDriveService` (currently `.../Services/MockOneDriveService.cs`) — no-Graph-auth
  substitute used when SharePoint isn't configured or auth is mocked.
- `GraphFolderResolver` (currently `.../Services/GraphFolderResolver.cs`, `internal` class) — moves
  together with `GraphOneDriveService` since it is a private implementation detail used only by
  that class (folder-ID resolution/caching for Graph move operations).

Target physical location: `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/`, namespace
`Anela.Heblo.Application.Shared.Rag` (or a `.OneDrive` sub-namespace — see Open Questions,
same convention decision as FR-1).

**Acceptance criteria:**
- `IOneDriveService`, `OneDriveFile`, `GraphOneDriveService`, `MockOneDriveService`, and
  `GraphFolderResolver` no longer live under any `Anela.Heblo.Application.Features.KnowledgeBase.*`
  namespace.
- `GraphFolderResolver` remains `internal` (assembly-level visibility is unaffected by the
  namespace move since both old and new locations are in the same `Anela.Heblo.Application`
  assembly).
- All existing Graph API call behavior, caching, and logging in `GraphOneDriveService` is preserved
  unchanged.
- Existing tests (`GraphOneDriveServiceTests` under
  `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/`, plus any `MockOneDriveService`/
  `GraphFolderResolver` coverage) are updated to the new namespace/location and continue to pass.

### FR-3: Update all consumers to the new namespace
Update every `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` (and
`...Services.DocumentExtractors;`) statement that refers only to the relocated types to
`using Anela.Heblo.Application.Shared.Rag;` (or the chosen sub-namespace). Files confirmed to need
updates:

Leaflet module (the actual boundary violation):
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletStatusTransitionTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/UploadLeafletHandlerTests.cs`

KnowledgeBase module (still consumes the same services, just via the new shared namespace):
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` (DI
  registration — see FR-4 for whether registration itself should also move)
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`
  (this file itself stays in `KnowledgeBase.Services` since `DocumentIndexingService`/
  `IDocumentIndexingService` are genuinely KnowledgeBase-owned; only its `using` for
  `IDocumentTextExtractor` changes — note it currently has no explicit `using` for
  `KnowledgeBase.Services` because it's declared in that same namespace today; after the move it
  will need an explicit `using Anela.Heblo.Application.Shared.Rag;`)
- `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`
- `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/GraphOneDriveServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PdfTextExtractorTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/WordDocumentExtractorTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`

Files that reference `Anela.Heblo.Application.Features.KnowledgeBase.Services` for **other**
(non-relocated) types — `ChatTranscriptPreprocessor`, `IChunkSummarizer`/`ChunkSummarizer`,
`IConversationTopicSummarizer`/`ConversationTopicSummarizer`, `IIndexingStrategy` and its
implementations, `IDocumentIndexingService` — must **keep** their existing `using` statement (or
keep it alongside a new `using Anela.Heblo.Application.Shared.Rag;` if the same file also uses a
relocated type). Do not remove the KnowledgeBase.Services import from files that still need it.

**Acceptance criteria:**
- Solution builds with zero references to the relocated types under
  `Anela.Heblo.Application.Features.KnowledgeBase.Services` anywhere in `backend/src` or
  `backend/test`.
- `dotnet build` succeeds with no new warnings introduced.
- No functional/behavioral changes — this is a namespace-only refactor.

### FR-4: Decide and implement DI registration ownership
Today `KnowledgeBaseModule.AddKnowledgeBaseModule()` registers all three `IDocumentTextExtractor`
implementations and conditionally registers `IOneDriveService` (`GraphOneDriveService` vs.
`MockOneDriveService` based on `KnowledgeBase:OneDriveFolderMappings`, `UseMockAuth`, and
`BYPASS_JWT_VALIDATION` configuration). `LeafletModule.AddLeafletModule()` does **not** register
these services itself — Leaflet's runtime dependency on them is currently satisfied only because
`ApplicationModule.cs` always calls `AddSharedRagModule()`, `AddKnowledgeBaseModule()`, and
`AddLeafletModule()` together at the composition root (`backend/src/Anela.Heblo.Application/ApplicationModule.cs`,
lines ~61, 99, 101). This is itself a hidden coupling the boundary test does not catch (DI
registration, not a compile-time type reference) and should be resolved as part of this fix, not
left in place.

Move the registration of the relocated services into `SharedRagModule.AddSharedRagModule()`
(`backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`), including the
Graph-vs-Mock `IOneDriveService` selection logic currently in `KnowledgeBaseModule` (this requires
passing `IConfiguration` into `AddSharedRagModule`, which it does not currently accept — signature
change required). Remove the corresponding registrations from `KnowledgeBaseModule.cs`.

**Acceptance criteria:**
- `IDocumentTextExtractor` (all 3 implementations) and `IOneDriveService` (Graph/Mock selection)
  are registered exclusively in `SharedRagModule`, not in `KnowledgeBaseModule`.
- `AddSharedRagModule` accepts `IConfiguration` (or an equivalent options-binding mechanism) so it
  can perform the same Graph-vs-Mock selection `KnowledgeBaseModule` does today, including the
  `services.AddHttpClient("MicrosoftGraph")` and `services.AddMemoryCache()` calls currently
  conditional on `sharePointConfigured && !useMockAuth && !bypassJwtValidation`.
- `ApplicationModule.cs` is updated if the `AddSharedRagModule()` call signature changes.
- Existing runtime behavior (which `IOneDriveService` implementation is selected under which
  configuration) is unchanged — verify via existing integration/DI tests
  (`HebloWebApplicationFactory.cs` and any composition-root smoke tests).
- KnowledgeBase and Leaflet both continue to resolve `IDocumentTextExtractor` and
  `IOneDriveService` successfully via DI after the change (no `InvalidOperationException` /
  missing-registration failures at startup or in integration tests).

### FR-5: Remove the resolved allowlist entries and verify the boundary test
Remove all four entries from `LeafletAllowlist` in
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (lines 37–38 and 44–45),
along with the now-obsolete justification comment block (lines 32–36 and 40–43), since the
underlying violation no longer exists.

**Acceptance criteria:**
- `LeafletAllowlist` no longer contains any `IDocumentTextExtractor`, `IOneDriveService`, or
  `OneDriveFile` entries.
- The `Consumer_types_should_not_reference_provider_owned_namespaces` theory test case for
  `"Leaflet -> KnowledgeBase"` passes with the allowlist empty (or removed entirely if it becomes
  empty — team's call, see Open Questions).
- Full `ModuleBoundariesTests` test class passes (all other module-boundary rules are unaffected by
  this change and must continue to pass as-is).

## Non-Functional Requirements

### NFR-1: Zero behavioral change
This is purely a structural/namespace refactor. No business logic, DI lifetime (`Scoped` vs
`Singleton`), Graph API call semantics, caching behavior, error handling, or logging behavior may
change as a side effect of the move. Any accidental behavior change discovered during the refactor
must be treated as a bug and either fixed to restore original behavior or explicitly called out and
approved separately — it is out of scope for this change.

### NFR-2: Build and test integrity
`dotnet build` and `dotnet format` must both pass cleanly after the change (per repository
validation standard). All existing tests that reference the relocated types (listed in FR-3) must
be updated in the same change set — a partial rename that leaves the solution non-compiling is not
acceptable as an intermediate state to commit.

### NFR-3: No new cross-module coupling introduced
The fix must not simply move the violation elsewhere — e.g., it must not make `KnowledgeBase`
depend on a new `Leaflet`-owned namespace, and it must not introduce a new dependency from
`Anela.Heblo.Application.Shared.*` back into any `Features.*` namespace. `Shared.Rag` types may
depend on `Anela.Heblo.Domain.Shared.*`, `Anela.Heblo.Application.Common.*` (e.g.
`GraphApiHelpers`), and standard framework/NuGet packages (Microsoft.Graph/Identity.Web bits,
`Microsoft.Extensions.*`), consistent with what `GraphOneDriveService` already depends on today.

## Data Model
No data model changes. This is a source-code namespace/DI-registration refactor only:
- No new database tables, columns, or migrations.
- No changes to `OneDriveFile`'s shape (`Id`, `Name`, `ContentType`, `Path`) — it is moved, not
  redesigned.
- No changes to `LeafletDocument`, `KnowledgeBaseDocument`, or any other domain entity.

## API / Interface Design
No public HTTP API surface is affected. This change is internal to the `Anela.Heblo.Application`
assembly:
- `IDocumentTextExtractor` interface shape is unchanged: `bool CanHandle(string contentType)`,
  `Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)`.
- `IOneDriveService` interface shape is unchanged: `ListInboxFilesAsync`, `DownloadFileAsync`,
  `MoveToArchivedAsync`, `DownloadFileTextByPathAsync`.
- `OneDriveFile` record shape is unchanged: `(string Id, string Name, string ContentType, string Path)`.
- Only the namespace (and physical file location) of these types, their implementations, and their
  DI registration changes.

## Dependencies
- Depends on the existing `Anela.Heblo.Application.Shared.Rag` namespace and
  `SharedRagModule`/`AddSharedRagModule()` DI extension already present in the codebase.
- Depends on `Anela.Heblo.Application.Common.Graph.GraphApiHelpers`, which is not moved and
  continues to be referenced by `GraphOneDriveService`/`GraphFolderResolver` from their new
  location.
- No new external library or package dependencies are introduced.
- No changes required to `docs/architecture/development_guidelines.md`'s module-boundary rules
  themselves — this change conforms to the existing rule rather than altering it. Consider a
  follow-up doc note in that file's examples of shared-infrastructure placement, but that is
  optional polish, not required for this fix.

## Out of Scope
- Any other pre-existing allowlist entries in `ModuleBoundariesTests.cs` (Logistics→Manufacture,
  Catalog→Purchase, Catalog→Manufacture, DataQuality→Catalog, DataQuality→Invoices,
  Manufacture→Catalog, ShoptetApi adapters, Packaging→ShoptetOrders, etc.) — untouched.
- Relocating `GraphApiHelpers` or any other `Common/Graph` helper — these remain shared utilities
  in their current location and are out of scope.
- Any change to `ChatTranscriptPreprocessor`, `ChunkSummarizer`, `ConversationTopicSummarizer`,
  `IIndexingStrategy`/implementations, or `DocumentIndexingService`/`IDocumentIndexingService` —
  these remain genuinely KnowledgeBase-owned and stay in
  `Anela.Heblo.Application.Features.KnowledgeBase.Services`.
- Any behavioral change to Graph API calls, retry/caching logic, or OneDrive folder resolution.
- Introducing a `ILeafletKnowledgeSource`-style adapter pattern for these two services — the
  brief's suggested fix (shared namespace relocation) is the chosen approach precisely because
  these are generic infrastructure services, not module-specific business contracts.
- Any UI/frontend changes — this is a pure backend/Application-layer refactor with no
  externally visible surface.

## Open Questions
1. **Exact target namespace/sub-namespacing convention.** This spec proposes
   `Anela.Heblo.Application.Shared.Rag` as the parent namespace (matching the existing
   `SharedRagModule`/`Domain.Shared.Rag.DocumentType` precedent), with implementations grouped
   under `DocumentExtractors/` and `OneDrive/` subfolders. The brief itself floated an alternative
   of two separate namespaces (`Application.Shared.Documents` and `Application.Shared.OneDrive` or
   `Domain.Shared`). Recommend confirming with the architect whether to fold these into the
   existing `Shared.Rag` family (this spec's assumption, since both services exist solely to
   support RAG ingestion pipelines today) versus creating new top-level `Shared.Documents` /
   `Shared.OneDrive` namespaces as the brief suggests. Either is mechanically straightforward; the
   choice affects only namespace strings and folder paths, not any other part of this spec.
2. **Test file physical location.** Should relocated types' unit tests move from
   `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/...` to a new
   `backend/test/Anela.Heblo.Tests/Shared/Rag/...` (or similar) location to mirror the new
   namespace, or is it acceptable to leave test files in place and just update their `using`
   statements? Recommend moving them for consistency, but this has no functional impact either way.
3. **`AddSharedRagModule` signature change.** FR-4 requires passing `IConfiguration` into
   `AddSharedRagModule()`, which currently takes no parameters. Confirm this is an acceptable
   breaking change to that internal extension method's signature (only one call site,
   `ApplicationModule.cs`, so blast radius is minimal) versus introducing a second overload or a
   separate `AddSharedOneDriveModule(configuration)` call.

## Status: HAS_QUESTIONS
