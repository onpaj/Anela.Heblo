# Relocate IDocumentTextExtractor / IOneDriveService to Shared.Rag Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `IDocumentTextExtractor` (+3 impls) and `IOneDriveService`/`OneDriveFile` (+3 impls/helpers) out of `Anela.Heblo.Application.Features.KnowledgeBase.Services` into `Anela.Heblo.Application.Shared.Rag` (and its `.DocumentExtractors`/`.OneDrive` sub-namespaces), move their DI registration into `SharedRagModule`, and remove the four now-obsolete `LeafletAllowlist` entries in `ModuleBoundariesTests.cs` — closing the Leaflet→KnowledgeBase compile-time boundary violation without introducing an adapter layer.

**Architecture:** Pure move/rename plus a DI-registration ownership change inside the `Anela.Heblo.Application` assembly. No interface shapes change, no behavior changes. Four sequential tasks, each leaving the solution in a compiling, test-passing state: (1) relocate the document-extractor family, (2) relocate the OneDrive-service family (including the mis-filed `GraphApiHelpers.cs` → `GraphDriveModels.cs` rename), (3) move DI registration ownership from `KnowledgeBaseModule` to `SharedRagModule` (which requires `AddSharedRagModule` to accept `IConfiguration`), (4) remove the boundary-test allowlist entries and do final verification (`dotnet format` across the whole change).

**Tech Stack:** .NET 8, C#, MediatR, Microsoft.Extensions.DependencyInjection, Microsoft.Identity.Web (Graph), xUnit, Moq, FluentAssertions.

---

## Reference: full file inventory (for all tasks)

**Moved (git mv), namespace changed:**

| From | To | New namespace |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/IDocumentTextExtractor.cs` | `Anela.Heblo.Application.Shared.Rag` |
| `.../Services/DocumentExtractors/PdfTextExtractor.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/PdfTextExtractor.cs` | `Anela.Heblo.Application.Shared.Rag.DocumentExtractors` |
| `.../Services/DocumentExtractors/WordDocumentExtractor.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/WordDocumentExtractor.cs` | `Anela.Heblo.Application.Shared.Rag.DocumentExtractors` |
| `.../Services/DocumentExtractors/PlainTextExtractor.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/PlainTextExtractor.cs` | `Anela.Heblo.Application.Shared.Rag.DocumentExtractors` |
| `.../Services/IOneDriveService.cs` (interface + `OneDriveFile` record) | `backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs` | `Anela.Heblo.Application.Shared.Rag` |
| `.../Services/GraphOneDriveService.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphOneDriveService.cs` | `Anela.Heblo.Application.Shared.Rag.OneDrive` |
| `.../Services/GraphFolderResolver.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphFolderResolver.cs` | `Anela.Heblo.Application.Shared.Rag.OneDrive` |
| `.../Services/MockOneDriveService.cs` | `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/MockOneDriveService.cs` | `Anela.Heblo.Application.Shared.Rag.OneDrive` |
| `.../Services/GraphApiHelpers.cs` (declares `GraphDriveItem`/`GraphFileFacet`/`GraphDriveItemCollection` — **not** the real `Common/Graph/GraphApiHelpers`) | `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs` | `Anela.Heblo.Application.Shared.Rag.OneDrive` |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PdfTextExtractorTests.cs` | `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PdfTextExtractorTests.cs` | `Anela.Heblo.Tests.Shared.Rag.DocumentExtractors` |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/WordDocumentExtractorTests.cs` | `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/WordDocumentExtractorTests.cs` | `Anela.Heblo.Tests.Shared.Rag.DocumentExtractors` |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs` | `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PlainTextExtractorTests.cs` | `Anela.Heblo.Tests.Shared.Rag.DocumentExtractors` |
| `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/GraphOneDriveServiceTests.cs` | `backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs` | `Anela.Heblo.Tests.Shared.Rag.OneDrive` |

**NOT moved** — stays in `Anela.Heblo.Application.Features.KnowledgeBase.Services` (genuinely KnowledgeBase-owned): `ChatTranscriptPreprocessor`, `IChunkSummarizer`/`ChunkSummarizer`, `IConversationTopicSummarizer`/`ConversationTopicSummarizer`, `IIndexingStrategy`/`KnowledgeBaseDocIndexingStrategy`/`ConversationIndexingStrategy`, `IDocumentIndexingService`/`DocumentIndexingService`. Do not touch these files' own namespace declarations — only add/adjust `using` statements where they reference a relocated type.

**NOT moved** — `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs` (the *real*, unrelated shared Graph helper with `EncodePath`/`CreateRequest`/`GraphBaseUrl`/`DeserializeAsync`, also used by `MeetingTasks`/`CatalogDocuments`). `GraphOneDriveService.cs` and `GraphFolderResolver.cs` keep their `using Anela.Heblo.Application.Common.Graph;` for this — do not remove it.

**Consumers requiring a `using` change** (verified by reading each file's current imports and usage before writing this plan):

*IDocumentTextExtractor consumers:*
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/UploadLeafletHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletStatusTransitionTests.cs`

*IOneDriveService / OneDriveFile / GraphOneDriveService / MockOneDriveService consumers:*
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`
- `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs`

**Confirmed to need NO change** (they only reference the non-relocated `IDocumentIndexingService`/`IIndexingStrategy` family from `KnowledgeBase.Services`, verified by reading each file): `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`, `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`. Do not edit these two files — their existing `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` is for `IDocumentIndexingService`, which is not moving.

---

### task: relocate-document-extractors

[Move IDocumentTextExtractor + PdfTextExtractor/WordDocumentExtractor/PlainTextExtractor to Shared.Rag(.DocumentExtractors), fix every consumer]

- [ ] Step 1: Create the target directory and move the interface.

  ```bash
  mkdir -p backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/IDocumentTextExtractor.cs
  ```

  Edit `backend/src/Anela.Heblo.Application/Shared/Rag/IDocumentTextExtractor.cs` — change the namespace line:

  ```csharp
  namespace Anela.Heblo.Application.Shared.Rag;

  public interface IDocumentTextExtractor
  {
      bool CanHandle(string contentType);
      Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default);
  }
  ```

- [ ] Step 2: Move the three extractor implementations.

  ```bash
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors/PdfTextExtractor.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/PdfTextExtractor.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors/WordDocumentExtractor.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/WordDocumentExtractor.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors/PlainTextExtractor.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/PlainTextExtractor.cs
  rmdir backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentExtractors 2>/dev/null || true
  ```

  In each of the three moved files, change only the `namespace` line from
  `namespace Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;`
  to
  `namespace Anela.Heblo.Application.Shared.Rag.DocumentExtractors;`
  Leave every other line (usings, class bodies, `CanHandle`/`ExtractTextAsync` logic, the `PdfTextExtractor.CleanPageText` regex helper) byte-for-byte unchanged.

- [ ] Step 3: Update `KnowledgeBaseModule.cs` imports (registration itself is not moving yet — that's a later task; only fix the `using`s so the file compiles against the new namespace).

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

  Remove this line:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
  ```

  Add these two lines (keep the existing `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` — it is still needed for `ChatTranscriptPreprocessor`, `IChunkSummarizer`, `IConversationTopicSummarizer`, `IIndexingStrategy`, `KnowledgeBaseDocIndexingStrategy`, `ConversationIndexingStrategy`, `IDocumentIndexingService`, `DocumentIndexingService`, which are not moving):
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
  ```

  Do not change any of the `services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();` etc. registration lines in this step — they will resolve correctly once the usings are in place.

- [ ] Step 4: Update the two Leaflet handlers.

  `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

  `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs` — same change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  →
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 5: Update `UploadDocumentHandler.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (Keep the other existing usings — `Anela.Heblo.Application.Features.KnowledgeBase;`, `Anela.Heblo.Application.Features.KnowledgeBase.Contracts;`, `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;`, `Anela.Heblo.Application.Shared;` — unchanged. This file only references `IDocumentTextExtractor` from the KB.Services namespace, nothing else, so a straight swap is correct.)

- [ ] Step 6: Update `DocumentIndexingService.cs` — this file currently has **no** explicit `using` for `KnowledgeBase.Services` because it is declared inside that namespace; after this move it needs a new, explicit using.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`

  Current top of file:
  ```csharp
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```

  Change to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```

  Do not change anything else in this file — `IEnumerable<IDocumentTextExtractor> _extractors` and all other members are unaffected in behavior, only in where `IDocumentTextExtractor` resolves from.

- [ ] Step 7: `dotnet build` the solution and confirm it succeeds with these `src` changes (test project will still fail to compile until Step 8 — that's expected and fixed next).

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```

  It is acceptable (and expected) that `Anela.Heblo.Tests` fails to build at this point with errors like `CS0246: The type or namespace name 'IDocumentTextExtractor' could not be found` in the test files touched in Step 8. `Anela.Heblo.Application`, `Anela.Heblo.API`, `Anela.Heblo.Domain`, and `Anela.Heblo.Persistence` must all build cleanly.

- [ ] Step 8: Move and fix the three extractor unit test files.

  ```bash
  mkdir -p backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors
  git mv backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PdfTextExtractorTests.cs \
         backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PdfTextExtractorTests.cs
  git mv backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/WordDocumentExtractorTests.cs \
         backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/WordDocumentExtractorTests.cs
  git mv backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs \
         backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PlainTextExtractorTests.cs
  ```

  In `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PdfTextExtractorTests.cs`, change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
  using Microsoft.Extensions.Logging.Abstractions;

  namespace Anela.Heblo.Tests.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
  using Microsoft.Extensions.Logging.Abstractions;

  namespace Anela.Heblo.Tests.Shared.Rag.DocumentExtractors;
  ```

  In `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/WordDocumentExtractorTests.cs`, change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
  using Microsoft.Extensions.Logging.Abstractions;

  namespace Anela.Heblo.Tests.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
  using Microsoft.Extensions.Logging.Abstractions;

  namespace Anela.Heblo.Tests.Shared.Rag.DocumentExtractors;
  ```
  (The old `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` line is dropped entirely — this test file never referenced `IDocumentTextExtractor` or any other KB.Services member directly, only `WordDocumentExtractor` from the `.DocumentExtractors` sub-namespace.)

  In `backend/test/Anela.Heblo.Tests/Shared/Rag/DocumentExtractors/PlainTextExtractorTests.cs`, apply the identical change (same before/after usings as `WordDocumentExtractorTests.cs` above, plus keep the pre-existing `using System.Text;` line at the top), and change the namespace to `Anela.Heblo.Tests.Shared.Rag.DocumentExtractors;`.

- [ ] Step 9: Update the remaining test files that consume `IDocumentTextExtractor` directly (via `Mock<IDocumentTextExtractor>`).

  In each of the following four files, change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (leaving every other `using` and all test bodies unchanged):
  - `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletHandlerTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletStatusTransitionTests.cs`
  - `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/UploadLeafletHandlerTests.cs`
  - `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`

- [ ] Step 10: Update `DocumentIndexingServiceTests.cs`, which uses both `IDocumentTextExtractor` (relocated) and `IIndexingStrategy` (not relocated) — it must keep the `KnowledgeBase.Services` using and add the new one.

  File: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`

  Current top:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;
  ```
  Change to:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;
  using Microsoft.Extensions.Options;
  using Moq;
  using Xunit;
  ```
  (Keep `Anela.Heblo.Application.Features.KnowledgeBase.Services;` — it is still required for `IIndexingStrategy`, `ChatTranscriptPreprocessor`, and `DocumentIndexingService` itself, all referenced later in this file.)

- [ ] Step 11: `dotnet build` the whole solution and confirm success.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors.

- [ ] Step 12: Run the affected test classes and confirm they pass.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Shared.Rag.DocumentExtractors|FullyQualifiedName~DocumentIndexingServiceTests|FullyQualifiedName~UploadDocumentHandlerTests|FullyQualifiedName~UploadLeafletHandlerTests|FullyQualifiedName~IndexLeafletHandlerTests|FullyQualifiedName~IndexLeafletStatusTransitionTests" \
    2>&1 | tail -60
  ```
  Expect all listed test classes to pass with 0 failures.

- [ ] Step 13: Commit.

  ```bash
  git add -A
  git commit -m "Relocate IDocumentTextExtractor and its 3 implementations to Shared.Rag"
  ```

---

### task: relocate-onedrive-services

[Move IOneDriveService/OneDriveFile, GraphOneDriveService, MockOneDriveService, GraphFolderResolver, and the mis-filed GraphApiHelpers.cs (renamed GraphDriveModels.cs) to Shared.Rag(.OneDrive), fix every consumer]

- [ ] Step 1: Create the target directory and move the interface + record.

  ```bash
  mkdir -p backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs
  ```

  Edit `backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs` — change only the namespace line:

  ```csharp
  namespace Anela.Heblo.Application.Shared.Rag;

  public record OneDriveFile(string Id, string Name, string ContentType, string Path);

  public interface IOneDriveService
  {
      Task<List<OneDriveFile>> ListInboxFilesAsync(string driveId, string inboxPath, CancellationToken ct = default);
      Task<byte[]> DownloadFileAsync(string driveId, string fileId, CancellationToken ct = default);
      Task<string> MoveToArchivedAsync(string driveId, string fileId, string filename, string archivedPath, CancellationToken ct = default);
      Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct = default);
  }
  ```

  Note: `OneDriveFile` stays a `record` — this project's "DTOs are classes" rule (CLAUDE.md) applies to API `Request`/`Response` contracts serialized through the OpenAPI client generator, not to this internal Application-layer service type. Do not convert it to a class.

- [ ] Step 2: Move `GraphOneDriveService.cs`, `GraphFolderResolver.cs`, and `MockOneDriveService.cs`.

  ```bash
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphOneDriveService.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphFolderResolver.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphFolderResolver.cs
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/MockOneDriveService.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/MockOneDriveService.cs
  ```

  In all three moved files, change only the `namespace` line from
  `namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;`
  to
  `namespace Anela.Heblo.Application.Shared.Rag.OneDrive;`

  Leave every other line unchanged, including the existing `using Anela.Heblo.Application.Common.Graph;` in `GraphOneDriveService.cs` and `GraphFolderResolver.cs` (this is the real, unrelated `Common/Graph/GraphApiHelpers` helper — it is not moving and both files keep depending on it exactly as today). `GraphFolderResolver` stays `internal` — no visibility change needed since old and new locations are both in the `Anela.Heblo.Application` assembly.

- [ ] Step 3: Move and rename the mis-filed DTO file.

  ```bash
  git mv backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphApiHelpers.cs \
         backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs
  ```

  Edit `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphDriveModels.cs` — change only the namespace line. Full expected file content after the edit:

  ```csharp
  using System.Text.Json.Serialization;

  namespace Anela.Heblo.Application.Shared.Rag.OneDrive;

  internal class GraphDriveItem
  {
      [JsonPropertyName("id")]
      public string Id { get; set; } = string.Empty;

      [JsonPropertyName("name")]
      public string Name { get; set; } = string.Empty;

      [JsonPropertyName("webUrl")]
      public string WebUrl { get; set; } = string.Empty;

      [JsonPropertyName("file")]
      public GraphFileFacet? File { get; set; }
  }

  internal class GraphFileFacet
  {
      [JsonPropertyName("mimeType")]
      public string MimeType { get; set; } = "application/octet-stream";
  }

  internal class GraphDriveItemCollection
  {
      [JsonPropertyName("value")]
      public List<GraphDriveItem> Value { get; set; } = [];
  }
  ```

  This file declares no `GraphApiHelpers` class (despite its old filename) — it only holds `GraphDriveItem`/`GraphFileFacet`/`GraphDriveItemCollection`, consumed directly by `GraphOneDriveService` and `GraphFolderResolver` in the same `Shared.Rag.OneDrive` namespace (no new `using` needed for them since they're in the same namespace after the move).

- [ ] Step 4: Update `KnowledgeBaseModule.cs` — add the OneDrive sub-namespace using (registration itself still lives here for now; only fix imports).

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

  Add this using (alongside the `Anela.Heblo.Application.Shared.Rag;` and `Anela.Heblo.Application.Shared.Rag.DocumentExtractors;` lines added in the previous task):
  ```csharp
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  ```
  (`IOneDriveService`/`OneDriveFile` now resolve via the already-present `using Anela.Heblo.Application.Shared.Rag;`; `GraphOneDriveService` and `MockOneDriveService` need the new `.OneDrive` using. Keep `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` — still needed for the non-relocated types.)

  Do not change the registration logic itself in this task (the `services.AddScoped<IOneDriveService, GraphOneDriveService>();` / `MockOneDriveService` block, `kbOptions`/`sharePointConfigured`/`useMockAuth`/`bypassJwtValidation` locals, and the conditional `AddHttpClient`/`AddMemoryCache` calls all stay put here — moving DI ownership is the next task).

- [ ] Step 5: Update `KnowledgeBaseIngestionJob.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (This file only injects `IOneDriveService` from `KnowledgeBase.Services` — no other member of that namespace — so a straight swap is correct.)

- [ ] Step 6: Update `KnowledgeBaseArticleStyleGuideSource.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 7: Update `LeafletIngestionJob.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 8: `dotnet build` and confirm `src` compiles (test project will still fail — expected until Step 10).

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  `Anela.Heblo.Application`, `Anela.Heblo.API`, `Anela.Heblo.Domain`, `Anela.Heblo.Persistence` must build with 0 errors.

- [ ] Step 9: Update `HebloWebApplicationFactory.cs`.

  File: `backend/test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  ```
  (This file references both `IOneDriveService` — resolves from `Shared.Rag` — and `MockOneDriveService` — resolves from `Shared.Rag.OneDrive` — in its DI-override block around `services.Where(s => s.ServiceType == typeof(IOneDriveService))` / `services.AddScoped<IOneDriveService, MockOneDriveService>();`. Do not change that override logic itself — only the imports.)

- [ ] Step 10: Move and fix `GraphOneDriveServiceTests.cs`.

  ```bash
  mkdir -p backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive
  git mv backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/GraphOneDriveServiceTests.cs \
         backend/test/Anela.Heblo.Tests/Shared/Rag/OneDrive/GraphOneDriveServiceTests.cs
  ```

  Change:
  ```csharp
  using System.Net;
  using System.Text;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Microsoft.Extensions.Caching.Memory;
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Identity.Web;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using System.Net;
  using System.Text;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  using Microsoft.Extensions.Caching.Memory;
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Identity.Web;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Shared.Rag.OneDrive;
  ```
  (`GraphOneDriveService` is the only relocated type this file references directly, and it now lives in `Shared.Rag.OneDrive`.)

- [ ] Step 11: Update the three remaining test files that reference `IOneDriveService`/`OneDriveFile` directly.

  `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseIngestionJobTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

  `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```
  (Keep `using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;` unchanged — that's for `KnowledgeBaseArticleStyleGuideSource` itself, which is not moving.)

  `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs` — change:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  ```
  to:
  ```csharp
  using Anela.Heblo.Application.Shared.Rag;
  ```

- [ ] Step 12: `dotnet build` the whole solution and confirm success.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors.

- [ ] Step 13: Run the affected test classes and confirm they pass.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Shared.Rag.OneDrive|FullyQualifiedName~KnowledgeBaseIngestionJobTests|FullyQualifiedName~KnowledgeBaseArticleStyleGuideSourceTests|FullyQualifiedName~LeafletIngestionJobTests" \
    2>&1 | tail -60
  ```
  Expect all listed test classes to pass with 0 failures.

- [ ] Step 14: Commit.

  ```bash
  git add -A
  git commit -m "Relocate IOneDriveService, its implementations, and Graph DTOs to Shared.Rag.OneDrive"
  ```

---

### task: move-di-registration-to-sharedragmodule

[Change AddSharedRagModule to accept IConfiguration and own all IDocumentTextExtractor/IOneDriveService registrations; strip them from KnowledgeBaseModule; update the one ApplicationModule.cs call site]

- [ ] Step 1: Rewrite `SharedRagModule.cs`.

  File: `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`

  Current content:
  ```csharp
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Shared.Rag;

  public static class SharedRagModule
  {
      public static IServiceCollection AddSharedRagModule(this IServiceCollection services)
      {
          services.AddScoped<IWordWindowChunker, WordWindowChunker>();
          services.AddScoped<IRagQueryExpander, RagQueryExpander>();
          return services;
      }
  }
  ```

  Replace with:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
  using Anela.Heblo.Application.Shared.Rag.OneDrive;
  using Anela.Heblo.Domain.Shared;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;

  namespace Anela.Heblo.Application.Shared.Rag;

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

          // OneDrive service — use real Graph service only when SharePoint drives are configured
          // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
          // would fail; MockOneDriveService is used in those environments instead.
          // Moved verbatim from KnowledgeBaseModule — the Graph-vs-Mock check intentionally still
          // only inspects the "KnowledgeBase" configuration section (not "Leaflet"), matching
          // today's behavior exactly. This is a pre-existing latent gap (Leaflet's own
          // OneDriveFolderMappings are never consulted here), tracked separately — not fixed as
          // part of this refactor (NFR-1: zero behavioral change).
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

  (`KnowledgeBaseOptions` lives in namespace `Anela.Heblo.Application.Features.KnowledgeBase` — confirmed by reading `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` — hence the new `using Anela.Heblo.Application.Features.KnowledgeBase;`. `InfrastructureConfigurationKeys` lives in `Anela.Heblo.Domain.Shared` — confirmed by reading `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` — hence `using Anela.Heblo.Domain.Shared;`. No `ModuleBoundariesTests` rule inspects the `Anela.Heblo.Application.Shared.Rag` namespace as a consumer, so this reference does not trip any existing boundary test.)

- [ ] Step 2: Update the single call site in `ApplicationModule.cs`.

  File: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

  Change:
  ```csharp
  services.AddSharedRagModule();
  ```
  to:
  ```csharp
  services.AddSharedRagModule(configuration);
  ```
  (`configuration` is already an in-scope parameter of `AddApplicationServices` — no other changes needed on this line. This is the only call site in the repository; confirm with `grep -rn "AddSharedRagModule(" backend/src backend/test` after this step — it must show exactly one match, in this file, now with the `configuration` argument.)

- [ ] Step 3: Strip the moved registrations out of `KnowledgeBaseModule.cs`, keeping the `KnowledgeBaseOptions` binding.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

  Remove these lines entirely:
  ```csharp
  services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
  services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
  services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
  ```
  and the whole OneDrive selection block:
  ```csharp
  // OneDrive service — use real Graph service only when SharePoint drives are configured
  // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
  // would fail; MockOneDriveService is used in those environments instead.
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
  ```

  Keep everything else unchanged, in particular:
  ```csharp
  services.AddOptions<KnowledgeBaseOptions>()
      .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
  (this is KnowledgeBase's own options binding, unrelated to the OneDrive Graph/Mock selection that just moved) and all the other registrations (`ChatTranscriptPreprocessor`, `IChunkSummarizer`, `IConversationTopicSummarizer`, `IIndexingStrategy` x2, `IDocumentIndexingService`, the `ILeafletKnowledgeSource`/`IArticleStyleGuideSource`/`IArticleKnowledgeSource` adapter bindings, `IKnowledgeBaseRepository`, `IProductEnrichmentCache`, the `QuestionLoggingBehavior` pipeline behavior registration).

  Now remove the `using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;` and `using Anela.Heblo.Application.Shared.Rag.OneDrive;` lines added in the previous two tasks **only if** nothing else in this file still needs them — check first:
  ```bash
  grep -n "PdfTextExtractor\|WordDocumentExtractor\|PlainTextExtractor\|GraphOneDriveService\|MockOneDriveService" backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
  ```
  If this returns no matches (expected, since those registrations were just deleted), remove the two now-unused `using` lines. Keep `using Anela.Heblo.Application.Shared.Rag;` if anything in the file still references `IDocumentTextExtractor` or `IOneDriveService` by type name (it will not, after the registrations are deleted, unless another line in the file separately depends on them — verify with the same grep pattern extended to `IDocumentTextExtractor\|IOneDriveService`); if that also returns no matches, remove that using too.

- [ ] Step 4: `dotnet build` the whole solution and confirm success.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors and no new warnings (compare warning count against Step 12 of the previous task if unsure).

- [ ] Step 5: Run the composition-root smoke tests to confirm DI still resolves both services end-to-end (this is the acceptance-criteria check for FR-4 — "KnowledgeBase and Leaflet both continue to resolve `IDocumentTextExtractor` and `IOneDriveService` successfully via DI").

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ApplicationStartupTests" \
    2>&1 | tail -60
  ```
  Expect `Application_Should_Start_Successfully` and every `Controller_Should_Be_Resolvable` case to pass — a missing/duplicate DI registration for `IOneDriveService` or `IDocumentTextExtractor` would surface here as a startup or controller-resolution failure.

- [ ] Step 6: Also re-run the full set of tests touched by the previous two tasks, to catch any regression from the registration move.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Shared.Rag|FullyQualifiedName~KnowledgeBase|FullyQualifiedName~Leaflet" \
    2>&1 | tail -80
  ```
  Expect 0 failures.

- [ ] Step 7: Commit.

  ```bash
  git add -A
  git commit -m "Move IDocumentTextExtractor/IOneDriveService DI registration from KnowledgeBaseModule to SharedRagModule"
  ```

---

### task: clean-boundary-allowlist-and-verify

[Remove the 4 resolved LeafletAllowlist entries, run full backend test suite, dotnet format, final build]

- [ ] Step 1: Confirm the `"Leaflet -> KnowledgeBase"` boundary rule already passes with the current (non-empty) allowlist, isolating a genuine pass from an allowlist-editing mistake.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ModuleBoundariesTests" \
    2>&1 | tail -60
  ```
  Expect all `ModuleBoundariesTests` theory cases to pass, including `Consumer_types_should_not_reference_provider_owned_namespaces(rule: "Leaflet -> KnowledgeBase")`.

- [ ] Step 2: Remove the four resolved allowlist entries and their justification comment.

  File: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

  Change:
  ```csharp
  // Pre-existing allowlist for Leaflet → KnowledgeBase. Each entry needs a comment with the
  // justification. Entries should be removed as the underlying violations are fixed.
  //
  // Entry format: "{ConsumerFullyQualifiedTypeName} -> {ProviderTypeFullName}"
  //
  // Compiler-generated types (e.g. DisplayClasses for closures, state machines for async
  // methods) are automatically handled by matching against the declaring type's namespace
  // prefix below.
  private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal)
  {
      // Pre-existing dependency: UploadLeafletHandler and IndexLeafletHandler consume
      // IDocumentTextExtractor, which currently lives in
      // Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
      // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these
      // entries when IDocumentTextExtractor is relocated to a shared namespace.
      "Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet.UploadLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",
      "Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet.IndexLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",

      // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which
      // currently lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting
      // this is out of scope for the 2026-05-15 Leaflet decoupling. Track separately and
      // remove these entries when IOneDriveService is relocated to a shared namespace.
      "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
      "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
  };
  ```
  to:
  ```csharp
  // Allowlist for Leaflet → KnowledgeBase. Empty — IDocumentTextExtractor and IOneDriveService
  // were relocated to Anela.Heblo.Application.Shared.Rag, closing the compile-time dependency.
  private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal);
  ```

  Do not touch any other allowlist in this file (`ArticleAllowlist`, `LogisticsAllowlist`, `CatalogLogisticsAllowlist`, `CatalogPurchaseAllowlist`, `CatalogManufactureAllowlist`, `DataQualityCatalogAllowlist`, `DataQualityInvoicesAllowlist`, `ManufactureCatalogAllowlist`, `ExpeditionListLogisticsAllowlist`, `ShoptetApiAdaptersCatalogAllowlist`, `ShoptetApiAdaptersLogisticsAllowlist`, `PackagingShoptetOrdersAllowlist`) — all out of scope per the spec.

- [ ] Step 3: Run `ModuleBoundariesTests` again and confirm the `"Leaflet -> KnowledgeBase"` case still passes with the now-empty allowlist.

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~ModuleBoundariesTests" \
    2>&1 | tail -60
  ```
  Expect all cases green, including `"Leaflet -> KnowledgeBase"`. If it fails, the failure output lists the exact `Consumer -> Provider` violation still remaining — cross-check it against the file inventory at the top of this plan for a missed consumer, fix the missed `using`, and re-run this step before proceeding.

- [ ] Step 4: Confirm zero remaining references to the relocated types under the old namespace anywhere in the codebase (FR-3 acceptance criterion).

  ```bash
  grep -rn "Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor\|Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService\|Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile\|Anela.Heblo.Application.Features.KnowledgeBase.Services.GraphOneDriveService\|Anela.Heblo.Application.Features.KnowledgeBase.Services.MockOneDriveService" \
    backend/src backend/test --include=*.cs
  ```
  Expect no output. Also confirm the old physical files are gone:
  ```bash
  ls backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ | grep -i "DocumentTextExtractor\|OneDriveService\|GraphOneDriveService\|MockOneDriveService\|GraphFolderResolver\|GraphApiHelpers\|DocumentExtractors"
  ```
  Expect no output (the `DocumentExtractors/` subfolder should no longer exist under `Features/KnowledgeBase/Services/`, and none of the listed filenames should remain there).

- [ ] Step 5: Run `dotnet format` once across the whole solution (repository validation standard) and review the diff it produces.

  ```bash
  cd backend && dotnet format Anela.Heblo.sln 2>&1 | tail -40
  git diff --stat
  ```
  If `dotnet format` changes any file outside the ones touched by this plan, review the change — it should only be whitespace/using-order normalization. If it reformats unrelated pre-existing code, revert those specific unrelated hunks with `git checkout -- <file>` to keep this change surgical, per this repo's "touch only what the task requires" rule, and re-run `dotnet format` scoped to the touched projects only if needed:
  ```bash
  dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```

- [ ] Step 6: Final full build.

  ```bash
  cd backend && dotnet build Anela.Heblo.sln 2>&1 | tail -40
  ```
  Expect: `Build succeeded.` with 0 errors, 0 new warnings.

- [ ] Step 7: Run the full backend test suite.

  ```bash
  cd backend && dotnet test Anela.Heblo.sln 2>&1 | tail -100
  ```
  Expect 0 failed tests. (Full-suite run, not just the filtered subsets used in earlier tasks — this is the final NFR-2 gate: "All existing tests that reference the relocated types must be updated in the same change set.")

- [ ] Step 8: Commit.

  ```bash
  git add -A
  git commit -m "Remove resolved Leaflet->KnowledgeBase allowlist entries; dotnet format pass"
  ```

---

## Self-review checklist (completed while writing this plan)

- FR-1 (relocate `IDocumentTextExtractor` + 3 impls): covered by `relocate-document-extractors`, Steps 1–2, 8.
- FR-2 (relocate `IOneDriveService`/`OneDriveFile` + `GraphOneDriveService`/`MockOneDriveService`/`GraphFolderResolver`, plus the arch-review-amended `GraphApiHelpers.cs`→`GraphDriveModels.cs`): covered by `relocate-onedrive-services`, Steps 1–3, 10.
- FR-3 (update all consumers, keep non-relocated-type usings intact): covered across both relocation tasks' Steps 3–7/9/11, verified in `clean-boundary-allowlist-and-verify` Step 4. `IndexDocumentHandler.cs`/`IndexDocumentHandlerTests.cs` were deliberately excluded after confirming by direct file read that they only use the non-relocated `IDocumentIndexingService`.
- FR-4 (DI registration ownership + `IConfiguration` signature change): covered by `move-di-registration-to-sharedragmodule`, all steps.
- FR-5 (remove allowlist entries, verify boundary test): covered by `clean-boundary-allowlist-and-verify`, Steps 1–3.
- NFR-1 (zero behavioral change): enforced by moving code verbatim (no logic edits beyond namespace/using lines) in every step; Decision 3 from the arch review (keep the `"KnowledgeBase"`-only config section check, do not generalize to `"Leaflet"`) is preserved verbatim in `SharedRagModule.cs` Step 1.
- NFR-2 (build/test integrity, no non-compiling intermediate commit): each task ends with a `dotnet build` + targeted `dotnet test` step before its commit; the final task runs the full suite.
- NFR-3 (no new cross-module coupling in the wrong direction): `SharedRagModule.cs`'s new dependency on `Anela.Heblo.Application.Features.KnowledgeBase` (for `KnowledgeBaseOptions`) and `Anela.Heblo.Domain.Shared` (for `InfrastructureConfigurationKeys`) was cross-checked against `ModuleBoundariesTests.cs`'s `Rules()` table — no rule inspects `Anela.Heblo.Application.Shared.Rag` as a consumer namespace, so this does not trip any boundary test; it also matches the arch review's own explicit Decision 2 code sample.
- Type/name consistency: `IDocumentTextExtractor`, `IOneDriveService`, `OneDriveFile`, `GraphOneDriveService`, `MockOneDriveService`, `GraphFolderResolver`, `GraphDriveModels`, `Anela.Heblo.Application.Shared.Rag`, `Anela.Heblo.Application.Shared.Rag.DocumentExtractors`, and `Anela.Heblo.Application.Shared.Rag.OneDrive` are spelled identically everywhere they appear across all four tasks.
- No placeholders: every step names exact files, exact before/after code, and exact commands.
