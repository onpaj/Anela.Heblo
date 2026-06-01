# RAG Knowledge Base UI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add claim-gated upload to the Knowledge Base UI: merge Search+Ask into one tab, gate delete & upload by `KnowledgeBase.Upload` Entra ID claim, add drag-and-drop upload tab.

**Architecture:** Backend adds a `KnowledgeBaseUpload` authorization policy and a `POST /api/knowledgebase/documents/upload` endpoint that runs the full text extraction → chunking → embedding → store pipeline inline. Frontend adds a permission hook reading MSAL `idTokenClaims`, merges the Search/Ask tabs into one component, conditionally renders delete and the Upload tab.

**Tech Stack:** .NET 8, MediatR, MVC, React 18, React Query, MSAL (`@azure/msal-react`), Tailwind CSS, lucide-react.

---

## Context: What Already Exists

Before starting, note what is **already done** so you don't redo it:

| Item | Status |
|------|--------|
| `GET /api/knowledgebase/documents` | ✅ Exists |
| `POST /api/knowledgebase/search` | ✅ Exists |
| `POST /api/knowledgebase/ask` | ✅ Exists |
| `DELETE /api/knowledgebase/documents/{id:guid}` | ✅ Exists (not yet claim-gated) |
| `KnowledgeBaseSearchTab.tsx` | ✅ Exists (will be replaced by merged tab) |
| `KnowledgeBaseAskTab.tsx` | ✅ Exists (will be replaced by merged tab) |
| `KnowledgeBaseDocumentsTab.tsx` | ✅ Exists (needs `canDelete` prop added) |
| `useKnowledgeBase.ts` hooks | ✅ Exists (need upload hook + permission hook added) |
| Route `/knowledge-base` in App.tsx | ✅ Exists |
| Sidebar nav item | ✅ Exists |
| `KnowledgeBasePage.tsx` | ✅ Exists (needs updating) |

**What is missing and needs to be built:**
1. `KnowledgeBaseUpload` authorization policy (backend)
2. `POST /api/knowledgebase/documents/upload` endpoint + handler (backend)
3. `useKnowledgeBaseUploadPermission` hook (frontend)
4. `useUploadKnowledgeBaseDocumentMutation` hook (frontend)
5. `KnowledgeBaseSearchAskTab.tsx` merged component (frontend)
6. `KnowledgeBaseUploadTab.tsx` (frontend)
7. Updates to `KnowledgeBaseDocumentsTab.tsx` and `KnowledgeBasePage.tsx` (frontend)

---

## Task 1: Add `KnowledgeBaseUpload` claim constant to Domain

**Files:**
- Read first: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`

**Step 1: Read the file**

Read `AuthorizationConstants.cs` to see current structure. It has a `Roles` class. Add a `Claims` class if not already present.

**Step 2: Add the claim constant**

Inside the `AuthorizationConstants` class, add:

```csharp
public static class Claims
{
    public const string KnowledgeBaseUpload = "KnowledgeBase.Upload";
}
```

**Step 3: Build to verify**

```bash
cd backend && dotnet build --no-restore -q
```
Expected: `Build succeeded. 0 Error(s)`

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git commit -m "feat: add KnowledgeBaseUpload claim constant"
```

---

## Task 2: Register `KnowledgeBaseUpload` authorization policy

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`

**Step 1: Add policy in `ConfigureAuthorizationPolicies`**

Find the `ConfigureAuthorizationPolicies` private method. It currently has only the default policy. Add a second policy after it:

```csharp
options.AddPolicy("KnowledgeBaseUpload", policy =>
    policy.RequireAuthenticatedUser()
          .RequireRole(AuthorizationConstants.Roles.HebloUser)
          .RequireClaim(AuthorizationConstants.Claims.KnowledgeBaseUpload));
```

The method should look like:

```csharp
private static void ConfigureAuthorizationPolicies(IServiceCollection services)
{
    services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(AuthorizationConstants.Roles.HebloUser)
            .Build();

        options.AddPolicy("KnowledgeBaseUpload", policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole(AuthorizationConstants.Roles.HebloUser)
                  .RequireClaim(AuthorizationConstants.Claims.KnowledgeBaseUpload));
    });
}
```

**Step 2: Build to verify**

```bash
cd backend && dotnet build --no-restore -q
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs
git commit -m "feat: register KnowledgeBaseUpload authorization policy"
```

---

## Task 3: Support `KnowledgeBase.Upload` claim in mock authentication

**Files:**
- Read first: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`
- Possibly modify: `backend/src/Anela.Heblo.API/appsettings.Development.json`

**Step 1: Read the mock handler**

Read `MockAuthenticationHandler.cs` to see how it builds the claims identity. It currently assigns the `HebloUser` role. You need to also add the `KnowledgeBase.Upload` claim for local development.

**Step 2: Add the upload claim**

Find where claims are built (look for `new Claim(...)` calls). Add:

```csharp
new Claim(AuthorizationConstants.Claims.KnowledgeBaseUpload, "true"),
```

Add it in the same block as the role claim. This means all mock-authenticated users in local dev will have upload permission — which is fine since mock auth is dev-only.

**Step 3: Build to verify**

```bash
cd backend && dotnet build --no-restore -q
```
Expected: `Build succeeded. 0 Error(s)`

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs
git commit -m "feat: add KnowledgeBaseUpload claim to mock authentication handler"
```

---

## Task 4: Create `UploadDocument` handler

**Files:**
- Read first: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentTextExtractor.cs`
- Read first: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentChunker.cs`
- Read first: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`

**Step 1: Read the service interfaces**

Read `IDocumentTextExtractor.cs` and `IDocumentChunker.cs` to get the exact method signatures before writing the handler. Also read `KnowledgeBaseDocument.cs` to see all required constructor fields.

**Step 2: Create `UploadDocumentRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentRequest : IRequest<UploadDocumentResponse>
{
    public Stream FileStream { get; set; } = default!;
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSizeBytes { get; set; }
}
```

**Step 3: Create `UploadDocumentResponse.cs`**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentResponse
{
    public bool Success { get; set; }
    public DocumentSummary? Document { get; set; }
}
```

**Step 4: Create `UploadDocumentHandler.cs`**

Adapt method signatures below to match what you found when reading the interfaces in Step 1.

```csharp
using System.Security.Cryptography;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentHandler : IRequestHandler<UploadDocumentRequest, UploadDocumentResponse>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IDocumentTextExtractor _extractor;
    private readonly IDocumentChunker _chunker;
    private readonly IEmbeddingService _embeddingService;

    public UploadDocumentHandler(
        IKnowledgeBaseRepository repository,
        IDocumentTextExtractor extractor,
        IDocumentChunker chunker,
        IEmbeddingService embeddingService)
    {
        _repository = repository;
        _extractor = extractor;
        _chunker = chunker;
        _embeddingService = embeddingService;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Buffer stream for hashing and text extraction
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        // Deduplicate by SHA-256 content hash
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        var existing = await _repository.GetDocumentByHashAsync(hash, cancellationToken);
        if (existing != null)
        {
            return new UploadDocumentResponse { Success = true, Document = MapToSummary(existing) };
        }

        // Create document record in "processing" state
        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}",
            ContentType = request.ContentType,
            ContentHash = hash,
            Status = "processing",
            CreatedAt = DateTime.UtcNow,
        };
        await _repository.AddDocumentAsync(doc, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            // Extract text from file bytes
            using var extractStream = new MemoryStream(fileBytes);
            // Adapt the call below to match IDocumentTextExtractor's actual signature
            var text = await _extractor.ExtractTextAsync(extractStream, request.ContentType, cancellationToken);

            // Chunk text
            // Adapt the call below to match IDocumentChunker's actual signature
            var chunkTexts = _chunker.Chunk(text).ToList();

            // Embed each chunk and create chunk entities
            var chunks = new List<KnowledgeBaseChunk>();
            foreach (var chunkText in chunkTexts)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkText, cancellationToken);
                chunks.Add(new KnowledgeBaseChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = doc.Id,
                    Content = chunkText,
                    Embedding = embedding,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await _repository.AddChunksAsync(chunks, cancellationToken);
            doc.Status = "indexed";
            doc.IndexedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            doc.Status = "failed";
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }

        return new UploadDocumentResponse { Success = true, Document = MapToSummary(doc) };
    }

    private static DocumentSummary MapToSummary(KnowledgeBaseDocument doc) =>
        new()
        {
            Id = doc.Id,
            Filename = doc.Filename,
            Status = doc.Status,
            ContentType = doc.ContentType,
            CreatedAt = doc.CreatedAt,
            IndexedAt = doc.IndexedAt,
        };
}
```

**Step 5: Build to verify**

```bash
cd backend && dotnet build --no-restore -q
```
Expected: `Build succeeded. 0 Error(s)`

If there are interface mismatch errors, read the actual interface files and adjust the method calls.

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/
git commit -m "feat: add UploadDocumentHandler with text extraction and embedding pipeline"
```

---

## Task 5: Add upload endpoint and gate delete endpoint in controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

**Step 1: Add upload using directive**

Add at the top:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
```

**Step 2: Gate the existing delete endpoint**

Find the `DeleteDocument` action and add `[Authorize(Policy = "KnowledgeBaseUpload")]`:

```csharp
[HttpDelete("documents/{id:guid}")]
[Authorize(Policy = "KnowledgeBaseUpload")]
public async Task<ActionResult<DeleteDocumentResponse>> DeleteDocument(Guid id, CancellationToken ct)
{
    var result = await _mediator.Send(new DeleteDocumentRequest { DocumentId = id }, ct);
    return HandleResponse(result);
}
```

**Step 3: Add upload endpoint**

Add after the delete action:

```csharp
[HttpPost("documents/upload")]
[Authorize(Policy = "KnowledgeBaseUpload")]
public async Task<ActionResult<UploadDocumentResponse>> UploadDocument(
    IFormFile file,
    CancellationToken ct)
{
    await using var stream = file.OpenReadStream();
    var request = new UploadDocumentRequest
    {
        FileStream = stream,
        Filename = file.FileName,
        ContentType = file.ContentType,
        FileSizeBytes = file.Length,
    };
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```

**Step 4: Build to verify**

```bash
cd backend && dotnet build --no-restore -q
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Run backend tests**

```bash
cd backend && dotnet test -q
```
Expected: All existing tests still pass.

**Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "feat: add upload endpoint and gate delete by KnowledgeBaseUpload policy"
```

---

## Task 6: Write backend test for `UploadDocumentHandler`

**Files:**
- Read first: `backend/test/Anela.Heblo.Tests/KnowledgeBase/` (see existing test patterns)
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UploadDocumentHandlerTests.cs`

**Step 1: Read an existing handler test for patterns**

Read any existing test in `backend/test/Anela.Heblo.Tests/KnowledgeBase/` to understand the mock library used (NSubstitute/Moq) and assertion library (FluentAssertions/Shouldly).

**Step 2: Write failing test first**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using FluentAssertions;
using NSubstitute;

namespace Anela.Heblo.Tests.KnowledgeBase;

public class UploadDocumentHandlerTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IDocumentTextExtractor _extractor = Substitute.For<IDocumentTextExtractor>();
    private readonly IDocumentChunker _chunker = Substitute.For<IDocumentChunker>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private UploadDocumentHandler CreateHandler() =>
        new(_repository, _extractor, _chunker, _embedding);

    [Fact]
    public async Task Handle_WhenDocumentAlreadyExistsByHash_ReturnExistingWithoutReindexing()
    {
        // Arrange
        var existing = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = "test.pdf",
            Status = "indexed",
            ContentHash = "any",
            SourcePath = "upload/test.pdf",
            ContentType = "application/pdf",
            CreatedAt = DateTime.UtcNow,
        };

        _repository.GetDocumentByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns(existing);

        var handler = CreateHandler();
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("content"u8.ToArray()),
            Filename = "test.pdf",
            ContentType = "application/pdf",
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Document!.Filename.Should().Be("test.pdf");
        await _repository.DidNotReceive().AddDocumentAsync(Arg.Any<KnowledgeBaseDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewDocument_IndexesAndReturnsIndexedStatus()
    {
        // Arrange
        _repository.GetDocumentByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                   .Returns((KnowledgeBaseDocument?)null);

        // Adapt the mock call below to match IDocumentTextExtractor's actual method signature
        _extractor.ExtractTextAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns("extracted text content");

        // Adapt mock call below to match IDocumentChunker's actual method signature
        _chunker.Chunk(Arg.Any<string>())
                .Returns(new[] { "chunk one", "chunk two" });

        _embedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns(new float[] { 0.1f, 0.2f, 0.3f });

        var handler = CreateHandler();
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("pdf content"u8.ToArray()),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Document!.Status.Should().Be("indexed");
        result.Document.Filename.Should().Be("guide.pdf");
        await _repository.Received(1).AddDocumentAsync(Arg.Any<KnowledgeBaseDocument>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).AddChunksAsync(
            Arg.Is<IEnumerable<KnowledgeBaseChunk>>(chunks => chunks.Count() == 2),
            Arg.Any<CancellationToken>());
    }
}
```

**Step 3: Run test to verify it fails first**

```bash
cd backend && dotnet test --filter "UploadDocumentHandlerTests" -v 2>&1 | tail -20
```
Expected: Tests not found yet (file just created) or fails due to interface mismatches.

**Step 4: Fix any interface mismatches and run again**

```bash
cd backend && dotnet test --filter "UploadDocumentHandlerTests" -v
```
Expected: Both tests pass.

**Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UploadDocumentHandlerTests.cs
git commit -m "test: add UploadDocumentHandler tests"
```

---

## Task 7: Add `useKnowledgeBaseUploadPermission` hook (Frontend)

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`

The pattern for reading MSAL claims is already in `frontend/src/auth/useAuth.ts` (line ~54):
```typescript
const idTokenClaims = account.idTokenClaims as any;
const roles = idTokenClaims?.roles || [];
```

Custom claims (non-role) appear directly on the `idTokenClaims` object with their exact claim name as key.

**Step 1: Add the MSAL import at the top of `useKnowledgeBase.ts`**

```typescript
import { useMsal } from '@azure/msal-react';
```

**Step 2: Add the permission hook**

Add before the `knowledgeBaseKeys` object:

```typescript
/**
 * Returns true when the current MSAL account has the KnowledgeBase.Upload custom claim.
 * Controls visibility of the Upload tab and delete buttons.
 */
export const useKnowledgeBaseUploadPermission = (): boolean => {
  const { accounts } = useMsal();
  const account = accounts[0];
  if (!account) return false;
  const claims = account.idTokenClaims as Record<string, unknown> | undefined;
  return Boolean(claims?.['KnowledgeBase.Upload']);
};
```

**Step 3: Run frontend type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: No errors.

**Step 4: Write a unit test**

Find the existing test file for `useKnowledgeBase` hooks (likely at `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`). Add:

```typescript
describe('useKnowledgeBaseUploadPermission', () => {
  it('returns true when KnowledgeBase.Upload claim is present', () => {
    // Mock useMsal to return an account with the claim
    // Use whatever mock pattern exists in the test file for @azure/msal-react
    mockUseMsal([{ idTokenClaims: { 'KnowledgeBase.Upload': 'true', roles: ['HebloUser'] } }]);
    const { result } = renderHook(() => useKnowledgeBaseUploadPermission());
    expect(result.current).toBe(true);
  });

  it('returns false when claim is absent', () => {
    mockUseMsal([{ idTokenClaims: { roles: ['HebloUser'] } }]);
    const { result } = renderHook(() => useKnowledgeBaseUploadPermission());
    expect(result.current).toBe(false);
  });

  it('returns false when no account is signed in', () => {
    mockUseMsal([]);
    const { result } = renderHook(() => useKnowledgeBaseUploadPermission());
    expect(result.current).toBe(false);
  });
});
```

**Step 5: Run tests**

```bash
cd frontend && npm test -- --testPathPattern="useKnowledgeBase" --watchAll=false
```
Expected: All tests pass.

**Step 6: Commit**

```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts
git commit -m "feat: add useKnowledgeBaseUploadPermission hook"
```

---

## Task 8: Add `useUploadKnowledgeBaseDocumentMutation` hook (Frontend)

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`

**Step 1: Add the response type**

Add to the types section:

```typescript
export interface UploadDocumentResponse {
  success: boolean;
  document: DocumentSummary | null;
}
```

**Step 2: Add the mutation hook**

Add after `useDeleteKnowledgeBaseDocumentMutation`:

```typescript
/**
 * Upload a file to the knowledge base.
 * Sends multipart/form-data to POST /api/knowledgebase/documents/upload.
 * Invalidates the documents list on success.
 */
export const useUploadKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (file: File): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/upload`;

      const formData = new FormData();
      formData.append('file', file);

      // Do NOT set Content-Type header — browser sets it with multipart boundary automatically
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error(`Upload failed: ${response.status}`);
      }

      return response.json();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents() });
    },
  });
};
```

**Step 3: Run type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: No errors.

**Step 4: Commit**

```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts
git commit -m "feat: add useUploadKnowledgeBaseDocumentMutation hook"
```

---

## Task 9: Create `KnowledgeBaseSearchAskTab.tsx` (merged tab)

**Files:**
- Create: `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx`

This replaces both `KnowledgeBaseSearchTab.tsx` and `KnowledgeBaseAskTab.tsx`. The old files are kept in place (they will simply no longer be imported).

**Step 1: Create the file**

```typescript
import React, { useState } from 'react';
import { Search, ChevronDown, ChevronUp } from 'lucide-react';
import { useKnowledgeBaseAskMutation, SourceReference } from '../../api/hooks/useKnowledgeBase';

const SourceAccordion: React.FC<{ sources: SourceReference[] }> = ({ sources }) => {
  const [open, setOpen] = useState(false);
  if (sources.length === 0) return null;

  return (
    <div className="border border-gray-200 rounded-lg overflow-hidden">
      <button
        onClick={() => setOpen((v) => !v)}
        className="w-full flex items-center justify-between px-4 py-2 bg-gray-50 text-sm font-medium text-gray-700 hover:bg-gray-100"
      >
        <span>Zdroje ({sources.length})</span>
        {open ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
      </button>
      {open && (
        <div className="divide-y divide-gray-100">
          {sources.map((src) => (
            <div key={src.documentId} className="px-4 py-3 space-y-1">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-gray-700">{src.filename}</span>
                <span className="text-xs px-1.5 py-0.5 rounded font-medium bg-gray-100 text-gray-600">
                  {Math.round(src.score * 100)}%
                </span>
              </div>
              <p className="text-xs text-gray-500 italic line-clamp-3">{src.excerpt}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

const KnowledgeBaseSearchAskTab: React.FC = () => {
  const [query, setQuery] = useState('');
  const ask = useKnowledgeBaseAskMutation();

  const handleSubmit = () => {
    if (query.trim()) ask.mutate({ question: query.trim() });
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <textarea
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Zadejte otázku nebo hledaný výraz... (Enter pro odeslání)"
          rows={3}
          className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
        />
        <button
          onClick={handleSubmit}
          disabled={ask.isPending || !query.trim()}
          className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
        >
          <Search className="w-4 h-4" />
          Hledat
        </button>
      </div>

      {ask.isPending && (
        <div className="space-y-2 animate-pulse">
          <div className="h-4 bg-gray-100 rounded w-3/4" />
          <div className="h-4 bg-gray-100 rounded w-full" />
          <div className="h-4 bg-gray-100 rounded w-2/3" />
        </div>
      )}

      {ask.isError && (
        <div className="text-red-600 text-sm">Dotaz se nezdařil. Zkuste to znovu.</div>
      )}

      {ask.data && (
        <div className="space-y-4">
          <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
            <p className="text-sm text-gray-800 whitespace-pre-wrap">{ask.data.answer}</p>
          </div>
          <SourceAccordion sources={ask.data.sources} />
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseSearchAskTab;
```

**Step 2: Run type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: No errors.

**Step 3: Commit**

```bash
git add frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx
git commit -m "feat: add KnowledgeBaseSearchAskTab merging search and AI answer"
```

---

## Task 10: Update `KnowledgeBaseDocumentsTab.tsx` — conditional delete

**Files:**
- Modify: `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`

**Step 1: Add `canDelete` prop interface**

At the top of the file, add:

```typescript
interface Props {
  canDelete: boolean;
}
```

Change the component signature from:
```typescript
const KnowledgeBaseDocumentsTab: React.FC = () => {
```
to:
```typescript
const KnowledgeBaseDocumentsTab: React.FC<Props> = ({ canDelete }) => {
```

**Step 2: Conditionally render delete column header**

In the `<thead>`, change the last `<th>` from:
```typescript
<th className="px-4 py-2" />
```
to:
```typescript
{canDelete && <th className="px-4 py-2" />}
```

**Step 3: Conditionally render delete button in rows**

In the `<tbody>` rows, change the delete cell from:
```typescript
<td className="px-4 py-2 text-right">
  <button
    onClick={() => setPendingDelete(doc)}
    title="Smazat dokument"
    className="text-gray-400 hover:text-red-600 transition-colors"
  >
    <Trash2 className="w-4 h-4" />
  </button>
</td>
```
to:
```typescript
{canDelete && (
  <td className="px-4 py-2 text-right">
    <button
      onClick={() => setPendingDelete(doc)}
      title="Smazat dokument"
      className="text-gray-400 hover:text-red-600 transition-colors"
    >
      <Trash2 className="w-4 h-4" />
    </button>
  </td>
)}
```

**Step 4: Run type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: Error about KnowledgeBasePage not passing the `canDelete` prop — that's expected and will be fixed in Task 12.

**Step 5: Commit**

```bash
git add frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx
git commit -m "feat: conditionally render delete button by canDelete prop"
```

---

## Task 11: Create `KnowledgeBaseUploadTab.tsx`

**Files:**
- Create: `frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx`

**Step 1: Check if `react-dropzone` is available**

```bash
cd frontend && grep '"react-dropzone"' package.json
```

If not present, use native HTML5 drag-and-drop (`onDragOver`, `onDrop` events) — no extra dependency.

**Step 2: Create the component**

```typescript
import React, { useCallback, useRef, useState } from 'react';
import { Upload, X, FileText } from 'lucide-react';
import { useUploadKnowledgeBaseDocumentMutation } from '../../api/hooks/useKnowledgeBase';

const ACCEPTED_EXTENSIONS = '.pdf,.docx,.txt,.md';

interface Props {
  onUploadSuccess: () => void;
}

const KnowledgeBaseUploadTab: React.FC<Props> = ({ onUploadSuccess }) => {
  const [dragOver, setDragOver] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const upload = useUploadKnowledgeBaseDocumentMutation();

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) setSelectedFile(file);
  }, []);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) setSelectedFile(file);
  };

  const handleUpload = async () => {
    if (!selectedFile) return;
    try {
      await upload.mutateAsync(selectedFile);
      setSelectedFile(null);
      onUploadSuccess();
    } catch {
      // Error displayed via upload.isError below
    }
  };

  const handleCancel = () => {
    setSelectedFile(null);
    upload.reset();
  };

  return (
    <div className="space-y-4 max-w-lg">
      {!selectedFile ? (
        <div
          onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={handleDrop}
          onClick={() => fileInputRef.current?.click()}
          className={`border-2 border-dashed rounded-xl p-12 text-center cursor-pointer transition-colors ${
            dragOver
              ? 'border-blue-400 bg-blue-50'
              : 'border-gray-300 hover:border-gray-400 bg-gray-50'
          }`}
        >
          <Upload className="w-10 h-10 text-gray-400 mx-auto mb-3" />
          <p className="text-sm font-medium text-gray-700">Přetáhněte soubor sem</p>
          <p className="text-xs text-gray-500 mt-1">nebo</p>
          <p className="text-sm text-blue-600 mt-1 font-medium">Vybrat soubor</p>
          <p className="text-xs text-gray-400 mt-3">Podporované formáty: PDF, DOCX, TXT, MD</p>
          <input
            ref={fileInputRef}
            type="file"
            accept={ACCEPTED_EXTENSIONS}
            className="hidden"
            onChange={handleFileChange}
          />
        </div>
      ) : (
        <div className="border border-gray-200 rounded-xl p-6 space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <FileText className="w-5 h-5 text-gray-500" />
              <span className="text-sm font-medium text-gray-700">{selectedFile.name}</span>
            </div>
            <button
              onClick={handleCancel}
              disabled={upload.isPending}
              className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
            >
              <X className="w-4 h-4" />
            </button>
          </div>

          {upload.isPending && (
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div className="bg-blue-600 h-1.5 rounded-full animate-pulse w-2/3" />
            </div>
          )}

          {upload.isError && (
            <p className="text-sm text-red-600">Nahrávání se nezdařilo. Zkuste to znovu.</p>
          )}

          <div className="flex gap-2">
            <button
              onClick={handleUpload}
              disabled={upload.isPending}
              className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
            >
              <Upload className="w-4 h-4" />
              Nahrát
            </button>
            <button
              onClick={handleCancel}
              disabled={upload.isPending}
              className="px-4 py-2 border border-gray-300 text-sm rounded-lg hover:bg-gray-50 disabled:opacity-50"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseUploadTab;
```

**Step 3: Run type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: No errors.

**Step 4: Commit**

```bash
git add frontend/src/components/knowledge-base/KnowledgeBaseUploadTab.tsx
git commit -m "feat: add KnowledgeBaseUploadTab with drag-and-drop and file picker"
```

---

## Task 12: Update `KnowledgeBasePage.tsx`

**Files:**
- Read first: `frontend/src/pages/KnowledgeBasePage.tsx`
- Modify: `frontend/src/pages/KnowledgeBasePage.tsx`

**Step 1: Read the current page**

Read the file to understand its current tab structure before modifying.

**Step 2: Replace with updated implementation**

```typescript
import React, { useState } from 'react';
import KnowledgeBaseSearchAskTab from '../components/knowledge-base/KnowledgeBaseSearchAskTab';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseUploadTab from '../components/knowledge-base/KnowledgeBaseUploadTab';
import { useKnowledgeBaseUploadPermission } from '../api/hooks/useKnowledgeBase';

type Tab = 'search' | 'documents' | 'upload';

const KnowledgeBasePage: React.FC = () => {
  const canUpload = useKnowledgeBaseUploadPermission();
  const [activeTab, setActiveTab] = useState<Tab>('search');

  const tabs: { id: Tab; label: string }[] = [
    { id: 'search', label: 'Hledat' },
    { id: 'documents', label: 'Dokumenty' },
    ...(canUpload ? [{ id: 'upload' as Tab, label: 'Nahrát soubor' }] : []),
  ];

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold text-gray-900">Znalostní báze</h1>

      <div className="border-b border-gray-200">
        <nav className="flex gap-6" aria-label="Tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      <div className="pt-2">
        {activeTab === 'search' && <KnowledgeBaseSearchAskTab />}
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab canDelete={canUpload} />}
        {activeTab === 'upload' && canUpload && (
          <KnowledgeBaseUploadTab onUploadSuccess={() => setActiveTab('documents')} />
        )}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
```

**Step 3: Run type check**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -20
```
Expected: No errors.

**Step 4: Commit**

```bash
git add frontend/src/pages/KnowledgeBasePage.tsx
git commit -m "feat: update KnowledgeBasePage with merged tab and claim-gated upload"
```

---

## Task 13: Build validation

**Step 1: Backend build**

```bash
cd backend && dotnet build -q
```
Expected: `Build succeeded. 0 Error(s)`

**Step 2: Backend tests**

```bash
cd backend && dotnet test -q
```
Expected: All tests pass.

**Step 3: Backend format check**

```bash
cd backend && dotnet format --verify-no-changes
```
If it reports formatting issues, run `dotnet format` to fix, then commit.

**Step 4: Frontend type check**

```bash
cd frontend && npx tsc --noEmit
```
Expected: No errors.

**Step 5: Frontend build**

```bash
cd frontend && npm run build 2>&1 | tail -10
```
Expected: Build successful.

**Step 6: Frontend lint**

```bash
cd frontend && npm run lint 2>&1 | tail -10
```
Fix any lint errors, then commit with `fix: lint`.

**Step 7: Final commit log review**

```bash
git log --oneline -15
```
Confirm all tasks have clean commits.
