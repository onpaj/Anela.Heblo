# Move DocumentSummary to Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the `DocumentSummary` DTO class out of `GetDocumentsRequest.cs` into its own file under a shared `Contracts/` folder so it can be referenced by both GetDocuments and UploadDocument use cases without cross-use-case coupling.

**Architecture:** This is a pure mechanical refactor — no logic changes. The new file lives at `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs` with namespace `Anela.Heblo.Application.Features.KnowledgeBase.Contracts`. All files that previously resolved `DocumentSummary` via the GetDocuments namespace get an explicit `using` for the Contracts namespace.

**Tech Stack:** .NET 8, C#, MediatR, xUnit.

---

### task: move-documentsummary-to-contracts

**Files:**
- CREATE: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs`
- EDIT: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs`
- EDIT: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs`
- EDIT: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs`
- EDIT: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`
- EDIT: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs`

- [ ] Step 1: Create the Contracts directory and the new `DocumentSummary.cs` file.

  Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs` with this exact content:

  ```csharp
  namespace Anela.Heblo.Application.Features.KnowledgeBase.Contracts;

  public class DocumentSummary
  {
      public Guid Id { get; set; }
      public string Filename { get; set; } = string.Empty;
      public string Status { get; set; } = string.Empty;
      public string ContentType { get; set; } = string.Empty;
      public DateTime CreatedAt { get; set; }
      public DateTime? IndexedAt { get; set; }
      public Guid? FirstChunkId { get; set; }
  }
  ```

  Note: `public class`, NOT `public record`. No extra usings needed — `Guid` and `DateTime` are globally available in .NET 6+.

- [ ] Step 2: Remove the `DocumentSummary` class from `GetDocumentsRequest.cs` and add the Contracts using.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs`

  Replace the entire file content with:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
  using Anela.Heblo.Application.Shared;
  using MediatR;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

  public class GetDocumentsRequest : IRequest<GetDocumentsResponse>
  {
      public int PageNumber { get; set; } = 1;
      public int PageSize { get; set; } = 20;
      public string SortBy { get; set; } = "CreatedAt";
      public bool SortDescending { get; set; } = true;
      public string? FilenameFilter { get; set; }
      public string? StatusFilter { get; set; }
      public string? ContentTypeFilter { get; set; }
  }

  public class GetDocumentsResponse : BaseResponse
  {
      public List<DocumentSummary> Documents { get; set; } = [];
      public int TotalCount { get; set; }
      public int PageNumber { get; set; }
      public int PageSize { get; set; }
      public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
  }
  ```

  The `DocumentSummary` class definition (old lines 26–35) is gone. `GetDocumentsResponse` still compiles because the Contracts using brings `DocumentSummary` into scope.

- [ ] Step 3: Add the Contracts using to `GetDocumentsHandler.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs`

  Replace the existing using block at the top:

  ```csharp
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using MediatR;
  ```

  with:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using MediatR;
  ```

  Everything else in the file stays unchanged. The handler references `DocumentSummary` on line 48 (`new DocumentSummary { ... }`) — the new using resolves it.

- [ ] Step 4: Update the using in `UploadDocumentResponse.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs`

  Replace the entire file content with:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

  public class UploadDocumentResponse : BaseResponse
  {
      public DocumentSummary? Document { get; set; }
  }
  ```

  The old `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;` is replaced by the Contracts using.

- [ ] Step 5: Update the using in `UploadDocumentHandler.cs`.

  File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`

  Replace the using block at the top:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  ```

  with:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase;
  using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
  using Anela.Heblo.Application.Features.KnowledgeBase.Services;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
  using Anela.Heblo.Application.Shared;
  using MediatR;
  ```

  The `GetDocuments` using is removed; the Contracts using is added. Everything else in the file (the handler body and `MapToSummary` method) stays unchanged.

- [ ] Step 6: Add the Contracts using to `KnowledgeBaseControllerTests.cs`, keeping the GetDocuments using.

  File: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs`

  Replace the using block at the top:

  ```csharp
  using Anela.Heblo.API.Controllers;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;
  using MediatR;
  using Microsoft.AspNetCore.Http;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Xunit;
  ```

  with:

  ```csharp
  using Anela.Heblo.API.Controllers;
  using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Anela.Heblo.Domain.Shared.Rag;
  using MediatR;
  using Microsoft.AspNetCore.Http;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;
  using Moq;
  using Xunit;
  ```

  The `GetDocuments` using is RETAINED (the test file uses `GetDocumentsRequest`, `GetDocumentsResponse` from that namespace). The Contracts using is ADDED so `DocumentSummary` resolves from its new location.

  Everything else in the test file stays unchanged.

- [ ] Step 7: Build and format.

  Run from the repo root (or the backend directory):

  ```bash
  cd backend && dotnet build
  ```

  Expected: build succeeds with 0 errors. If there are errors, they will be "type or namespace not found" for `DocumentSummary` — double-check that the correct using was added to the failing file.

  Then run:

  ```bash
  cd backend && dotnet format
  ```

  Expected: exits 0, no formatting changes reported. If it reports changes, apply them (re-run with `--verify-no-changes` removed, or let it fix in-place) and confirm the build still passes.

- [ ] Step 8: Commit.

  ```bash
  git add \
    backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Contracts/DocumentSummary.cs \
    backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs \
    backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs \
    backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs \
    backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs \
    backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs

  git commit -m "refactor: move DocumentSummary DTO to KnowledgeBase Contracts namespace"
  ```
