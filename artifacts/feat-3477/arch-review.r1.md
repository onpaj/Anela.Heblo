# Architecture Review: Relocate `IDocumentTextExtractor` / `IOneDriveService` to `Shared.Rag`

## Skip Design: true

Pure backend namespace/DI refactor inside `Anela.Heblo.Application`. No controller, contract, or
frontend surface changes.

## Architectural Fit Assessment

The spec's approach is correct and is already the codebase's established pattern, not a new one:
`docs/architecture/filesystem.md` explicitly documents `Application/Shared/Rag/` as "Cross-module
RAG application/infrastructure types — options base classes, helpers, shared services
(`RagFeatureOptions`, `OneDriveFolderMapping`, `IRagQueryExpander`)" — i.e. this namespace was
created precisely to hold cross-cutting RAG infrastructure shared by KnowledgeBase and Leaflet.
`IDocumentTextExtractor` and `IOneDriveService` are exactly that category of type: generic
infrastructure consumed identically by both modules, not a module-owned business contract. The
`ILeafletKnowledgeSource`-style consumer-owned-contract pattern (`development_guidelines.md`,
"Cross-Module Communication Example") is the right tool when module A needs a *business*
capability from module B; it is the wrong tool here, and the spec correctly rejects it (see its
"Out of Scope" section) — wrapping a stateless file-format extractor in two per-module adapters
would add ceremony with no behavioral or ownership benefit.

`ModuleBoundariesTests.cs` enforces the rule via reflection over namespace prefixes
(`ForbiddenNamespacePrefixes: ["Anela.Heblo.Domain.Features.KnowledgeBase", "Anela.Heblo.Application.Features.KnowledgeBase", "Anela.Heblo.Persistence.KnowledgeBase"]`
for the `"Leaflet -> KnowledgeBase"` rule). Moving the four types out from under
`Anela.Heblo.Application.Features.KnowledgeBase.*` into `Anela.Heblo.Application.Shared.Rag.*`
mechanically satisfies the rule with no allowlist needed — this is a structural fix, not a
suppression.

I independently traced every consumer, the DI registration path, and the existing `Shared/Rag`
family in the actual source (not just the spec's description) and confirm the spec's factual
claims with one correction and one omission, both called out below.

**Correction to spec's background claim:** the spec states `Domain.Shared.Rag` hosts
`OneDriveFolderMapping`. That's not accurate — `OneDriveFolderMapping` lives in
`Anela.Heblo.Application/Shared/Rag/OneDriveFolderMapping.cs` (Application layer, confirmed by
`filesystem.md` too). `Domain.Shared.Rag` holds only `DocumentType`. This doesn't change the
spec's conclusion (Application-layer `Shared.Rag` is still the right home for these service
interfaces, since they depend on `Microsoft.Identity.Web`/`IMemoryCache`/`IHttpClientFactory`, all
Application-layer-appropriate dependencies that would violate Domain purity) but implementers
should not go looking for a separate `Domain.Shared.Rag.OneDriveFolderMapping`.

**Omission in FR-2:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs`
(the file, not to be confused with the real shared helper of the same class-file name at
`Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs`) is not in the spec's FR-2 file list, but
it must move. Despite its filename, it declares no `GraphApiHelpers` class at all — it holds three
`internal` Graph JSON DTOs (`GraphDriveItem`, `GraphFileFacet`, `GraphDriveItemCollection`) that
`GraphOneDriveService` and `GraphFolderResolver` both deserialize into directly. See Specification
Amendments.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application.Shared.Rag                  (existing: SharedRagModule, WordWindowChunker,
 │                                                    RagQueryExpander, RagFeatureOptions,
 │                                                    OneDriveFolderMapping)
 ├── IDocumentTextExtractor.cs                       (moved from KnowledgeBase.Services)
 ├── DocumentExtractors/
 │     ├── PdfTextExtractor.cs
 │     ├── WordDocumentExtractor.cs
 │     └── PlainTextExtractor.cs
 ├── IOneDriveService.cs  (+ OneDriveFile record)     (moved from KnowledgeBase.Services)
 ├── OneDrive/
 │     ├── GraphOneDriveService.cs
 │     ├── GraphFolderResolver.cs        (internal)
 │     ├── GraphDriveModels.cs           (internal DTOs — see Spec Amendments; was mis-filed
 │     │                                  as "GraphApiHelpers.cs")
 │     └── MockOneDriveService.cs
 └── SharedRagModule.cs                              (now takes IConfiguration; owns
                                                        IDocumentTextExtractor x3 + IOneDriveService
                                                        Graph/Mock selection)

Anela.Heblo.Application.Features.KnowledgeBase.Services  (unchanged ownership)
 ├── ChatTranscriptPreprocessor, ChunkSummarizer, ConversationTopicSummarizer,
 │   IIndexingStrategy (+2 impls), IDocumentIndexingService/DocumentIndexingService
 └── (imports Anela.Heblo.Application.Shared.Rag for IDocumentTextExtractor)

Anela.Heblo.Application.Features.Leaflet.*            (consumer — now has zero compile-time
                                                        reference to any KnowledgeBase namespace)
 ├── UseCases/UploadLeaflet/UploadLeafletHandler.cs   → uses Shared.Rag.IDocumentTextExtractor
 ├── UseCases/IndexLeaflet/IndexLeafletHandler.cs     → uses Shared.Rag.IDocumentTextExtractor
 └── Infrastructure/Jobs/LeafletIngestionJob.cs       → uses Shared.Rag.IOneDriveService/OneDriveFile
```

### Key Design Decisions

#### Decision 1: Target namespace — single `Shared.Rag` family, subdivided by physical folder (resolves Open Question 1)

**Options considered:**
- (a) Two new top-level namespaces, `Application.Shared.Documents` / `Application.Shared.OneDrive`
  (the brief's suggestion).
- (b) Fold everything flat into `Anela.Heblo.Application.Shared.Rag` with no sub-namespace.
- (c) Fold into `Anela.Heblo.Application.Shared.Rag`, using `.DocumentExtractors` and `.OneDrive`
  sub-namespaces that mirror physical subfolders (the spec's proposal, loosely).

**Chosen approach:** (c). `IDocumentTextExtractor` and `IOneDriveService`/`OneDriveFile` go
directly in `Anela.Heblo.Application.Shared.Rag`; their implementations go in
`Anela.Heblo.Application.Shared.Rag.DocumentExtractors` and
`Anela.Heblo.Application.Shared.Rag.OneDrive` respectively, physically under
`Shared/Rag/DocumentExtractors/` and `Shared/Rag/OneDrive/`.

**Rationale:** Option (a) is rejected — it invents a second shared-infrastructure family when one
already exists and is documented in `filesystem.md`; it would also strand `OneDriveFolderMapping`
(already in `Shared.Rag`) apart from `IOneDriveService`, which is confusing since a
`OneDriveFolderMapping.DriveId` is exactly what `IOneDriveService` methods take as a parameter.
Between (b) and (c): every existing precedent in this codebase — `KnowledgeBase/Services/DocumentExtractors/`
(folder) → `.Services.DocumentExtractors` (namespace), and every `{Feature}/UseCases/{UseCase}/`
folder → matching namespace segment — makes the physical folder *always* a literal namespace
segment. (b) would break that convention (folder `DocumentExtractors/` but namespace staying flat
`Shared.Rag`), which is inconsistent and would look like a mistake in code review. (c) is simply
"do what every other feature in this repo already does." This is not a coin flip — it's applying
the codebase's own established convention rather than picking a variant that isn't used anywhere
else.

#### Decision 2: DI registration ownership moves to `SharedRagModule`, keyed by `IConfiguration` (resolves Open Question 3)

**Options considered:**
- (a) Leave `AddSharedRagModule()` parameterless; add a second, separate
  `AddSharedRagOneDriveModule(configuration)` call for the new registrations.
- (b) Change `AddSharedRagModule()`'s signature to accept `IConfiguration`, matching the
  registration style of every other feature module in `ApplicationModule.cs`.

**Chosen approach:** (b).

**Rationale:** `AddSharedRagModule()` has exactly one call site
(`ApplicationModule.cs:61`, `services.AddSharedRagModule();`, which already has `configuration` in
scope as a method parameter) — this is confirmed by reading `ApplicationModule.cs` directly, so
blast radius is a one-line change, not a hunt across the codebase. Every other module registration
in that same file already takes `configuration` (`AddKnowledgeBaseModule(configuration)`,
`AddLeafletModule(configuration)`, `AddBankModule(configuration)`, etc.) — a parameterless
`AddSharedRagModule()` next to fourteen `Add{X}Module(configuration)` calls is already the odd one
out. Option (a) just relocates the "which parameter does this take" inconsistency into a second
method instead of removing it, and it would need its own call site wiring anyway. Change the
signature; there is nothing to preserve by not changing it.

```csharp
public static class SharedRagModule
{
    public static IServiceCollection AddSharedRagModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IWordWindowChunker, WordWindowChunker>();
        services.AddScoped<IRagQueryExpander, RagQueryExpander>();

        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();

        // Moved verbatim from KnowledgeBaseModule — see Decision 3 for why the
        // "KnowledgeBase" section name is preserved unchanged.
        var kbOptions = new KnowledgeBaseOptions();
        configuration.GetSection("KnowledgeBase").Bind(kbOptions);
        var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwtValidation = configuration.GetValue<bool>(InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION, false);

        if (sharePointConfigured && !useMockAuth && !bypassJwtValidation)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddMemoryCache();
            services.AddScoped<IOneDriveService, GraphOneDriveService>();
        }
        else
        {
            services.AddScoped<IOneDriveService, MockOneDriveService>();
        }

        return services;
    }
}
```

`ApplicationModule.cs:61` becomes `services.AddSharedRagModule(configuration);`.
`KnowledgeBaseModule.AddKnowledgeBaseModule` drops the `IDocumentTextExtractor` x3 registrations,
the `IOneDriveService` Graph/Mock block, the now-unused `services.AddHttpClient`/`AddMemoryCache`
calls tied to it, and the `kbOptions`/`sharePointConfigured`/`useMockAuth`/`bypassJwtValidation`
locals — but **keeps** its own `services.AddOptions<KnowledgeBaseOptions>().Bind(...)` block
unchanged (that's KnowledgeBase's own options binding, unrelated to the OneDrive selection logic
that's moving).

#### Decision 3: Preserve the existing single-section (`"KnowledgeBase"`) Graph-vs-Mock check verbatim — do not generalize it as part of this change

**Options considered:**
- (a) Move the check as-is: `IOneDriveService` selection still only inspects
  `configuration.GetSection("KnowledgeBase").Bind(...)`.
- (b) Generalize it while moving: check whether *either* `KnowledgeBase:OneDriveFolderMappings` or
  `Leaflet:OneDriveFolderMappings` has a configured `DriveId`, since `LeafletOptions` also extends
  `RagFeatureOptions` and independently configures its own `OneDriveFolderMappings` under the
  `"Leaflet"` section (confirmed by reading `LeafletOptions.cs`).

**Chosen approach:** (a).

**Rationale:** This is a pure relocation (NFR-1: "Zero behavioral change" — the spec is explicit
that any accidental behavior change discovered during the refactor is a bug, out of scope). Option
(b) would be a legitimate improvement — today, if KnowledgeBase's SharePoint drives are ever
unconfigured while Leaflet's remain configured, the *shared* `IOneDriveService` registration
silently falls back to `MockOneDriveService`, breaking Leaflet's ingestion for a reason that has
nothing to do with Leaflet's own config. But fixing that now conflates "move the type" with "fix a
latent cross-module bug," which is exactly the kind of scope creep NFR-1 rules out. See Risks table
below — this is flagged as a known latent gap, not silently fixed and not silently ignored.

## Implementation Guidance

### Directory / Module Structure

Physical moves (`git mv`, not copy+delete, to preserve blame history):

```
Features/KnowledgeBase/Services/IDocumentTextExtractor.cs
  → Shared/Rag/IDocumentTextExtractor.cs                       (namespace: Anela.Heblo.Application.Shared.Rag)

Features/KnowledgeBase/Services/DocumentExtractors/PdfTextExtractor.cs
Features/KnowledgeBase/Services/DocumentExtractors/WordDocumentExtractor.cs
Features/KnowledgeBase/Services/DocumentExtractors/PlainTextExtractor.cs
  → Shared/Rag/DocumentExtractors/*.cs                         (namespace: ...Shared.Rag.DocumentExtractors)

Features/KnowledgeBase/Services/IOneDriveService.cs
  → Shared/Rag/IOneDriveService.cs                             (namespace: Anela.Heblo.Application.Shared.Rag;
                                                                 contains both IOneDriveService and OneDriveFile)

Features/KnowledgeBase/Services/GraphOneDriveService.cs
Features/KnowledgeBase/Services/GraphFolderResolver.cs
Features/KnowledgeBase/Services/MockOneDriveService.cs
  → Shared/Rag/OneDrive/*.cs                                   (namespace: ...Shared.Rag.OneDrive)

Features/KnowledgeBase/Services/GraphApiHelpers.cs   [rename — see Spec Amendments]
  → Shared/Rag/OneDrive/GraphDriveModels.cs                    (namespace: ...Shared.Rag.OneDrive;
                                                                 internal GraphDriveItem/GraphFileFacet/
                                                                 GraphDriveItemCollection)
```

Do **not** touch `Features/KnowledgeBase/Services/GraphApiHelpers.cs`'s sibling,
`Application/Common/Graph/GraphApiHelpers.cs` — that one stays put (per spec, and confirmed: it's
also used by `MeetingTasks/Services/GraphPlannerService.cs` and
`CatalogDocuments/Services/GraphCatalogDocumentsStorage.cs`, well outside this change's scope).

Test moves (resolves Open Question 2 — **move**, mirroring the existing `Shared/Rag/` test
convention already in place for `RagFeatureOptionsTests.cs` / `RagQueryExpanderTests.cs` at
`backend/test/Anela.Heblo.Tests/Shared/Rag/`):

```
test/.../KnowledgeBase/Services/PdfTextExtractorTests.cs      → test/.../Shared/Rag/DocumentExtractors/PdfTextExtractorTests.cs
test/.../KnowledgeBase/Services/WordDocumentExtractorTests.cs → test/.../Shared/Rag/DocumentExtractors/WordDocumentExtractorTests.cs
test/.../KnowledgeBase/Services/PlainTextExtractorTests.cs    → test/.../Shared/Rag/DocumentExtractors/PlainTextExtractorTests.cs
test/.../KnowledgeBase/Services/GraphOneDriveServiceTests.cs  → test/.../Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs
```

Update each test file's namespace to `Anela.Heblo.Tests.Shared.Rag.DocumentExtractors` /
`Anela.Heblo.Tests.Shared.Rag.OneDrive` to match.

### Interfaces and Contracts

No shape changes — confirmed against the current source, byte-for-byte:

```csharp
namespace Anela.Heblo.Application.Shared.Rag;

public interface IDocumentTextExtractor
{
    bool CanHandle(string contentType);
    Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
}

public record OneDriveFile(string Id, string Name, string ContentType, string Path);

public interface IOneDriveService
{
    Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default);
    Task<string> MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default);
    Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct = default);
}
```

Note on `OneDriveFile` being a `record`: this project's DTO rule ("DTOs are classes, never C#
records" — CLAUDE.md, `development_guidelines.md`) applies to API `Request`/`Response` DTOs in a
module's `Contracts/` folder that flow through the OpenAPI client generator. `OneDriveFile` is an
internal Application-layer service type, never serialized over HTTP or exposed via a controller —
it is exactly the "internal domain types may still be records" carve-out. No change needed; do not
convert it to a class during the move.

### Data Flow

No change to runtime data flow — this section is unaffected by the move. Both use cases below
already work today; only the compile-time `using` and the DI registration owner change:

1. **Leaflet ingestion** (`LeafletIngestionJob`, a recurring `IRecurringJob`): resolves
   `IOneDriveService` (now `Shared.Rag`) → lists/downloads inbox files → hands bytes to
   `IndexLeafletHandler`/`UploadLeafletHandler`, which resolve `IEnumerable<IDocumentTextExtractor>`
   (now `Shared.Rag`) to pick the extractor whose `CanHandle(contentType)` matches → extracted text
   flows into Leaflet's own indexing pipeline. `IOneDriveService.MoveToArchivedAsync` archives the
   source file after processing.
2. **KnowledgeBase ingestion** (`KnowledgeBaseIngestionJob` → `IndexDocumentHandler`/
   `UploadDocumentHandler` → `DocumentIndexingService`): identical pattern, same two shared
   interfaces, same DI-resolved implementations — now sourced from `SharedRagModule` instead of
   `KnowledgeBaseModule`, with no change in which concrete type gets resolved at runtime.

### Test/DI-override impact

`backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` (line ~103) removes and
re-adds the `IOneDriveService` registration to force `MockOneDriveService` in the test host
regardless of environment config — this override targets the **service type**, not the module that
registered it, so it is unaffected by which module (`KnowledgeBaseModule` vs `SharedRagModule`)
originally added it. Its `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` (line 13)
must become `using Anela.Heblo.Application.Shared.Rag;` — already listed in the spec's FR-3 file
list; confirmed necessary by reading the file directly.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `GraphApiHelpers.cs` (KnowledgeBase.Services) is missing from spec FR-2's file list; if implementers follow FR-2 literally, `GraphOneDriveService`/`GraphFolderResolver` won't compile after the move (they reference `GraphDriveItem`/`GraphDriveItemCollection` from that file) | High (breaks the build if missed) | Explicit fix in this review: move it to `Shared/Rag/OneDrive/GraphDriveModels.cs`, called out in Directory/Module Structure above. Since it declares no `GraphApiHelpers` class, renaming avoids future confusion with the real `Common/Graph/GraphApiHelpers.cs` it sits next to. |
| Latent cross-module gap: the Graph-vs-Mock `IOneDriveService` selection only ever inspects `configuration.GetSection("KnowledgeBase")`, never `"Leaflet"`, even though both modules configure independent `OneDriveFolderMappings`. Preserved as-is per NFR-1, but it's a real correctness gap for the *shared* service | Medium (pre-existing, not introduced by this change) | Do not fix in this PR (see Decision 3). File a separate follow-up: either check across all `RagFeatureOptions`-derived sections, or have each consuming module register its own scoped `IOneDriveService` per drive/config rather than sharing one process-wide selection. |
| `IConfiguration` is now a required parameter of `AddSharedRagModule` — any test or tool that calls it directly (bypassing `AddApplicationServices`) breaks at compile time | Low (single call site confirmed via grep; no other callers found in `backend/src` or `backend/test`) | No action beyond updating the one call site in `ApplicationModule.cs`; grep for `AddSharedRagModule(` after the change as a final check. |
| Moving `IDocumentIndexingService`'s sibling file `DocumentIndexingService.cs` is explicitly out of scope, but it currently has *no* explicit `using` for `KnowledgeBase.Services` (it's declared inside that namespace) — easy to forget adding the new `using Anela.Heblo.Application.Shared.Rag;` since nothing "looks broken" until compilation | Medium | Already called out correctly in spec FR-3; this review confirms it by reading the file — `DocumentIndexingService.cs` has zero `using` statements referencing `KnowledgeBase.Services` today, so a fresh `using Anela.Heblo.Application.Shared.Rag;` must be added, not edited. |
| Reflection-based `ModuleBoundariesTests` theory for `"Leaflet -> KnowledgeBase"` currently has 4 allowlist entries; if any transitive KnowledgeBase reference survives the move (e.g. via a forgotten file), the test will surface it as a **new, unexplained** violation with an empty allowlist, which is harder to triage than today's annotated allowlist | Low | Run `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces` (the `"Leaflet -> KnowledgeBase"` case specifically) after FR-1–FR-3, before deleting the allowlist entries in FR-5, to get a clean pass/fail signal isolated from allowlist edits. |

## Specification Amendments

1. **FR-2 must include `GraphApiHelpers.cs`** (from `Features/KnowledgeBase/Services/`) in its
   relocation list. It is not mentioned anywhere in the spec, but it is a hard compile dependency
   of `GraphOneDriveService.cs` and `GraphFolderResolver.cs` (both deserialize into
   `GraphDriveItem`/`GraphDriveItemCollection`, defined in that file). Move it to
   `Shared/Rag/OneDrive/`. Recommend renaming the file to `GraphDriveModels.cs` in the same move
   (namespace-only rename, no behavior change) since it declares no `GraphApiHelpers` class and the
   filename collision with `Common/Graph/GraphApiHelpers.cs` (a different, real, unrelated shared
   helper that both `GraphOneDriveService` and `GraphFolderResolver` also import) is a pre-existing
   source of confusion this move can quietly resolve.
2. **Open Question 1 resolved:** sub-namespaces `Anela.Heblo.Application.Shared.Rag.DocumentExtractors`
   and `Anela.Heblo.Application.Shared.Rag.OneDrive`, mirroring physical `DocumentExtractors/` and
   `OneDrive/` subfolders — matching the codebase's universal folder-equals-namespace-segment
   convention. `IDocumentTextExtractor` and `IOneDriveService`/`OneDriveFile` themselves live
   directly under `Shared.Rag` (no subfolder), consistent with `IRagQueryExpander`/`IWordWindowChunker`
   already there today.
3. **Open Question 2 resolved:** move the test files, don't just retarget their `using`s. Target:
   `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/` and
   `backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/`, matching the existing
   `backend/test/Anela.Heblo.Tests/Shared/Rag/{RagFeatureOptionsTests,RagQueryExpanderTests}.cs`
   convention already in the repo.
4. **Open Question 3 resolved:** change `AddSharedRagModule()`'s signature to
   `AddSharedRagModule(this IServiceCollection services, IConfiguration configuration)` (breaking
   change accepted — one call site, matches every sibling module's signature convention).
5. **Correction:** the spec's claim that `Domain.Shared.Rag` hosts `OneDriveFolderMapping` is
   wrong — it's in `Application.Shared.Rag`. `Domain.Shared.Rag` holds only `DocumentType`. Doesn't
   change any decision, but implementers should not search Domain for it.
6. FR-4's acceptance criteria are otherwise sound as written and don't need amendment — the
   `KnowledgeBaseOptions` binding logic for the Graph-vs-Mock check should move **verbatim**
   (`configuration.GetSection("KnowledgeBase")`, unchanged), per Decision 3 above; this review makes
   explicit what the spec left implicit (that "unchanged runtime behavior" specifically means *not*
   generalizing the section name to cover Leaflet too).

## Prerequisites

None beyond what already exists in the repo — no new config, no migrations, no infrastructure.
`Anela.Heblo.Application.Shared.Rag` namespace, `SharedRagModule`, and the
`Anela.Heblo.Application.Common.Graph.GraphApiHelpers` helper this move depends on are all already
present and unaffected by this change. This can proceed directly to implementation.
