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

