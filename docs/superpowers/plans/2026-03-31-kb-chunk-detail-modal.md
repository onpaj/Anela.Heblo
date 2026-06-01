# KB Chunk Detail Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a modal that lets users browse the full content and metadata of any source chunk returned by the Knowledge Base Ask and Search tabs.

**Architecture:** New `GET /api/knowledgebase/chunks/{id}` endpoint backed by a `GetChunkDetailHandler` and a new repository method. Frontend adds a `useChunkDetailQuery` hook and a `ChunkDetailModal` component, wired into both the Ask and Search tabs via local state.

**Tech Stack:** .NET 8 / MediatR / EF Core (backend); React + TypeScript + TanStack Query (frontend); Tailwind CSS; Vitest + React Testing Library (frontend tests); xUnit + Moq (backend tests).

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs`
- `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`
- `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx`

**Modify:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add `KnowledgeBaseChunkNotFound = 2003`
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs` — add `GetChunkByIdAsync`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — implement `GetChunkByIdAsync`
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — add `GetChunkDetail` endpoint
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs` — add `ChunkId` to `SourceReference`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs` — populate `ChunkId` on `SourceReference`
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs` — add `GetChunkDetail` endpoint tests
- `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs` — add `GetChunkByIdAsync` integration test
- `frontend/src/api/hooks/useKnowledgeBase.ts` — add `ChunkDetail` types, `chunkDetail` query key, `useChunkDetailQuery` hook, `chunkId` to `SourceReference`
- `frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx` — add selected chunk state + modal trigger
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx` — add selected chunk state + modal trigger

---

### Task 1: Add `KnowledgeBaseChunkNotFound` error code

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`

- [ ] **Step 1: Add the error code**

  In `ErrorCodes.cs`, find the `// KnowledgeBase module errors (20XX)` section and add after `KnowledgeBaseFeedbackAlreadySubmitted = 2002`:

  ```csharp
  [HttpStatusCode(HttpStatusCode.NotFound)]
  KnowledgeBaseChunkNotFound = 2003,
  ```

- [ ] **Step 2: Build to verify no errors**

  ```bash
  cd backend && dotnet build --no-restore -q
  ```
  Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
  git commit -m "feat(kb): add KnowledgeBaseChunkNotFound error code"
  ```

---

### Task 2: Add `GetChunkByIdAsync` to repository interface and implementation

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

- [ ] **Step 1: Add method to interface**

  In `IKnowledgeBaseRepository.cs`, add after `Task DeleteDocumentAsync(...)`:

  ```csharp
  Task<KnowledgeBaseChunk?> GetChunkByIdAsync(Guid chunkId, CancellationToken ct = default);
  ```

- [ ] **Step 2: Implement in repository**

  In `KnowledgeBaseRepository.cs`, add after `DeleteDocumentAsync`:

  ```csharp
  public async Task<KnowledgeBaseChunk?> GetChunkByIdAsync(Guid chunkId, CancellationToken ct = default)
  {
      return await _context.KnowledgeBaseChunks
          .Include(c => c.Document)
          .FirstOrDefaultAsync(c => c.Id == chunkId, ct);
  }
  ```

  Add `using Microsoft.EntityFrameworkCore;` at the top if not already present (it is — EF Core is already used in the file).

- [ ] **Step 3: Build to verify**

  ```bash
  cd backend && dotnet build --no-restore -q
  ```
  Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs \
          backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
  git commit -m "feat(kb): add GetChunkByIdAsync to repository"
  ```

---

### Task 3: Create `GetChunkDetail` use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

  Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs`:

  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

  public class GetChunkDetailHandlerTests
  {
      private readonly Mock<IKnowledgeBaseRepository> _repository = new();

      private static KnowledgeBaseChunk MakeChunk(
          Guid? id = null,
          string content = "full conversation text",
          string summary = "AI-generated summary",
          int chunkIndex = 0,
          DocumentType documentType = DocumentType.Conversation) =>
          new()
          {
              Id = id ?? Guid.NewGuid(),
              DocumentId = Guid.NewGuid(),
              ChunkIndex = chunkIndex,
              Content = content,
              Summary = summary,
              DocumentType = documentType,
              Embedding = [],
              Document = new KnowledgeBaseDocument
              {
                  Id = Guid.NewGuid(),
                  Filename = "conversation-2024.txt",
                  SourcePath = "/inbox/conversation-2024.txt",
                  ContentType = "text/plain",
                  ContentHash = "abc123",
                  Status = DocumentStatus.Indexed,
                  DocumentType = documentType,
                  CreatedAt = DateTime.UtcNow.AddDays(-1),
                  IndexedAt = DateTime.UtcNow,
              }
          };

      [Fact]
      public async Task Handle_ReturnsChunkDetail_WhenChunkExists()
      {
          var chunkId = Guid.NewGuid();
          var chunk = MakeChunk(id: chunkId, content: "full text", summary: "summary");

          _repository
              .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(chunk);

          var handler = new GetChunkDetailHandler(_repository.Object);
          var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

          Assert.True(result.Success);
          Assert.Equal(chunkId, result.ChunkId);
          Assert.Equal("full text", result.Content);
          Assert.Equal("summary", result.Summary);
          Assert.Equal("conversation-2024.txt", result.Filename);
          Assert.Equal(DocumentType.Conversation, result.DocumentType);
          Assert.NotNull(result.IndexedAt);
      }

      [Fact]
      public async Task Handle_ReturnsNotFound_WhenChunkDoesNotExist()
      {
          var chunkId = Guid.NewGuid();

          _repository
              .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
              .ReturnsAsync((KnowledgeBaseChunk?)null);

          var handler = new GetChunkDetailHandler(_repository.Object);
          var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

          Assert.False(result.Success);
          Assert.Equal(ErrorCodes.KnowledgeBaseChunkNotFound, result.ErrorCode);
      }
  }
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd backend && dotnet test --filter "GetChunkDetailHandlerTests" --no-build 2>&1 | tail -5
  ```
  Expected: compile error — `GetChunkDetailHandler` not found.

- [ ] **Step 3: Create request/response types**

  Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`:

  ```csharp
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using MediatR;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;

  public class GetChunkDetailRequest : IRequest<GetChunkDetailResponse>
  {
      public Guid ChunkId { get; set; }
  }

  public class GetChunkDetailResponse : BaseResponse
  {
      public Guid ChunkId { get; set; }
      public Guid DocumentId { get; set; }
      public string Filename { get; set; } = string.Empty;
      public DocumentType DocumentType { get; set; }
      public DateTime? IndexedAt { get; set; }
      public int ChunkIndex { get; set; }
      public string Summary { get; set; } = string.Empty;
      public string Content { get; set; } = string.Empty;

      public GetChunkDetailResponse() { }

      public GetChunkDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
  }
  ```

- [ ] **Step 4: Create handler**

  Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`:

  ```csharp
  using Anela.Heblo.Application.Shared;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  using MediatR;

  namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;

  public class GetChunkDetailHandler : IRequestHandler<GetChunkDetailRequest, GetChunkDetailResponse>
  {
      private readonly IKnowledgeBaseRepository _repository;

      public GetChunkDetailHandler(IKnowledgeBaseRepository repository)
      {
          _repository = repository;
      }

      public async Task<GetChunkDetailResponse> Handle(
          GetChunkDetailRequest request,
          CancellationToken cancellationToken)
      {
          var chunk = await _repository.GetChunkByIdAsync(request.ChunkId, cancellationToken);

          if (chunk is null)
              return new GetChunkDetailResponse(ErrorCodes.KnowledgeBaseChunkNotFound);

          return new GetChunkDetailResponse
          {
              ChunkId = chunk.Id,
              DocumentId = chunk.DocumentId,
              Filename = chunk.Document.Filename,
              DocumentType = chunk.DocumentType,
              IndexedAt = chunk.Document.IndexedAt,
              ChunkIndex = chunk.ChunkIndex,
              Summary = chunk.Summary,
              Content = chunk.Content,
          };
      }
  }
  ```

- [ ] **Step 5: Run tests to verify they pass**

  ```bash
  cd backend && dotnet test --filter "GetChunkDetailHandlerTests" -q
  ```
  Expected: `2 passed`.

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/ \
          backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/GetChunkDetailHandlerTests.cs
  git commit -m "feat(kb): add GetChunkDetail use case"
  ```

---

### Task 4: Add controller endpoint + controller tests

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs`

- [ ] **Step 1: Write the failing controller tests**

  Open `KnowledgeBaseControllerTests.cs` and add these two test methods inside the `KnowledgeBaseControllerTests` class (after existing tests):

  ```csharp
  [Fact]
  public async Task GetChunkDetail_Returns200_WithChunkDetail()
  {
      // Arrange
      var chunkId = Guid.NewGuid();
      var expectedResponse = new GetChunkDetailResponse
      {
          Success = true,
          ChunkId = chunkId,
          DocumentId = Guid.NewGuid(),
          Filename = "conversation-2024.txt",
          DocumentType = DocumentType.Conversation,
          IndexedAt = DateTime.UtcNow,
          ChunkIndex = 0,
          Summary = "summary text",
          Content = "full conversation text",
      };

      _mockMediator
          .Setup(m => m.Send(It.Is<GetChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedResponse);

      // Act
      var result = await _controller.GetChunkDetail(chunkId, default);

      // Assert
      var okResult = Assert.IsType<OkObjectResult>(result.Result);
      var response = Assert.IsType<GetChunkDetailResponse>(okResult.Value);
      Assert.True(response.Success);
      Assert.Equal(chunkId, response.ChunkId);
      Assert.Equal("conversation-2024.txt", response.Filename);
  }

  [Fact]
  public async Task GetChunkDetail_Returns404_WhenChunkNotFound()
  {
      // Arrange
      var chunkId = Guid.NewGuid();
      var notFoundResponse = new GetChunkDetailResponse(ErrorCodes.KnowledgeBaseChunkNotFound);

      _mockMediator
          .Setup(m => m.Send(It.Is<GetChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
          .ReturnsAsync(notFoundResponse);

      // Act
      var result = await _controller.GetChunkDetail(chunkId, default);

      // Assert
      Assert.IsType<NotFoundObjectResult>(result.Result);
  }
  ```

  Also add the required using statements at the top of the file:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
  using Anela.Heblo.Domain.Features.KnowledgeBase;
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd backend && dotnet test --filter "GetChunkDetail_Returns" --no-build 2>&1 | tail -5
  ```
  Expected: compile error — `GetChunkDetail` action not found.

- [ ] **Step 3: Add endpoint to controller**

  In `KnowledgeBaseController.cs`, add the using statement:
  ```csharp
  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
  ```

  Add the action method after the `Search` action:

  ```csharp
  [HttpGet("chunks/{id:guid}")]
  public async Task<ActionResult<GetChunkDetailResponse>> GetChunkDetail(Guid id, CancellationToken ct)
  {
      var result = await _mediator.Send(new GetChunkDetailRequest { ChunkId = id }, ct);
      return HandleResponse(result);
  }
  ```

- [ ] **Step 4: Run tests to verify they pass**

  ```bash
  cd backend && dotnet test --filter "GetChunkDetail_Returns" -q
  ```
  Expected: `2 passed`.

- [ ] **Step 5: Run full backend test suite**

  ```bash
  cd backend && dotnet test -q
  ```
  Expected: all tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs \
          backend/test/Anela.Heblo.Tests/KnowledgeBase/Controllers/KnowledgeBaseControllerTests.cs
  git commit -m "feat(kb): add GET /api/knowledgebase/chunks/{id} endpoint"
  ```

---

### Task 5: Add `ChunkId` to `SourceReference` in Ask response

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`
- Modify (verify passing): `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`

- [ ] **Step 1: Add `ChunkId` to `SourceReference`**

  In `AskQuestionRequest.cs`, update `SourceReference`:

  ```csharp
  public class SourceReference
  {
      public Guid ChunkId { get; set; }
      public Guid DocumentId { get; set; }
      public string Filename { get; set; } = string.Empty;
      public string Excerpt { get; set; } = string.Empty;
      public double Score { get; set; }
  }
  ```

- [ ] **Step 2: Populate `ChunkId` in handler**

  In `AskQuestionHandler.cs`, update the `SourceReference` projection inside `Handle`:

  ```csharp
  Sources = searchResult.Chunks.Select(c => new SourceReference
  {
      ChunkId = c.ChunkId,
      DocumentId = c.DocumentId,
      Filename = c.SourceFilename,
      Excerpt = c.Content[..Math.Min(200, c.Content.Length)],
      Score = c.Score
  }).ToList()
  ```

- [ ] **Step 3: Run affected tests**

  ```bash
  cd backend && dotnet test --filter "AskQuestion" -q
  ```
  Expected: all pass.

- [ ] **Step 4: Commit**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/
  git commit -m "feat(kb): add ChunkId to AskQuestion SourceReference"
  ```

---

### Task 6: Add frontend types and `useChunkDetailQuery` hook

**Files:**
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`

- [ ] **Step 1: Add `ChunkDetail` types and extend `SourceReference`**

  In `useKnowledgeBase.ts`:

  1. Add `chunkId` to the existing `SourceReference` interface:

  ```typescript
  export interface SourceReference {
    chunkId: string;     // added
    documentId: string;
    filename: string;
    excerpt: string;
    score: number;
  }
  ```

  2. Add new types after the `FeedbackStatsDto` interface:

  ```typescript
  export interface ChunkDetail {
    chunkId: string;
    documentId: string;
    filename: string;
    documentType: 'KnowledgeBase' | 'Conversation';
    indexedAt: string | null;
    chunkIndex: number;
    summary: string;
    content: string;
  }

  export interface GetChunkDetailResponse {
    success: boolean;
    chunkId: string;
    documentId: string;
    filename: string;
    documentType: 'KnowledgeBase' | 'Conversation';
    indexedAt: string | null;
    chunkIndex: number;
    summary: string;
    content: string;
  }
  ```

- [ ] **Step 2: Add `chunkDetail` to the query key factory**

  In the `knowledgeBaseKeys` object, add:

  ```typescript
  chunkDetail: (chunkId: string) =>
    [...QUERY_KEYS.knowledgeBase, 'chunk-detail', chunkId] as const,
  ```

- [ ] **Step 3: Add `useChunkDetailQuery` hook**

  Add after `useKnowledgeBaseSearchMutation`:

  ```typescript
  /**
   * Fetch full chunk content and document metadata by chunk ID.
   * Only fires when chunkId is non-null.
   */
  export const useChunkDetailQuery = (chunkId: string | null) => {
    return useQuery({
      queryKey: knowledgeBaseKeys.chunkDetail(chunkId ?? ''),
      queryFn: async (): Promise<GetChunkDetailResponse> => {
        const apiClient = getAuthenticatedApiClient();
        const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/chunks/${chunkId}`;

        const response = await (apiClient as any).http.fetch(fullUrl, {
          method: 'GET',
          headers: { Accept: 'application/json' },
        });

        if (!response.ok) {
          throw new Error(`Failed to fetch chunk detail: ${response.status}`);
        }

        return response.json();
      },
      enabled: !!chunkId,
      staleTime: 10 * 60 * 1000,
      gcTime: 15 * 60 * 1000,
    });
  };
  ```

- [ ] **Step 4: Build frontend to verify no type errors**

  ```bash
  cd frontend && npm run build 2>&1 | tail -10
  ```
  Expected: build succeeds with no TypeScript errors.

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/api/hooks/useKnowledgeBase.ts
  git commit -m "feat(kb): add useChunkDetailQuery hook and ChunkDetail types"
  ```

---

### Task 7: Create `ChunkDetailModal` component

**Files:**
- Create: `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`
- Create: `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx`

- [ ] **Step 1: Write the failing tests**

  Create `frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx`:

  ```typescript
  import React from 'react';
  import { render, screen, fireEvent, waitFor } from '@testing-library/react';
  import { vi } from 'vitest';
  import ChunkDetailModal from '../ChunkDetailModal';
  import * as hooks from '../../../api/hooks/useKnowledgeBase';

  const mockChunkDetail = {
    success: true,
    chunkId: 'chunk-1',
    documentId: 'doc-1',
    filename: 'conversation-2024.txt',
    documentType: 'Conversation' as const,
    indexedAt: '2024-03-15T10:00:00Z',
    chunkIndex: 0,
    summary: 'This is an AI-generated summary of the conversation.',
    content: 'This is the full conversation text that can be very long.',
  };

  const mockOnClose = vi.fn();

  function renderModal(chunkId = 'chunk-1', score?: number) {
    return render(
      <ChunkDetailModal chunkId={chunkId} score={score} onClose={mockOnClose} />
    );
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  test('renders loading skeleton while fetching', () => {
    vi.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as any);

    renderModal();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // Loading state — no content yet
    expect(screen.queryByText('Shrnutí')).not.toBeInTheDocument();
  });

  test('renders summary and content when loaded', async () => {
    vi.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
      data: mockChunkDetail,
      isLoading: false,
      isError: false,
    } as any);

    renderModal('chunk-1', 0.87);

    expect(screen.getByText('conversation-2024.txt')).toBeInTheDocument();
    expect(screen.getByText('Shrnutí')).toBeInTheDocument();
    expect(screen.getByText('This is an AI-generated summary of the conversation.')).toBeInTheDocument();
    expect(screen.getByText('Obsah')).toBeInTheDocument();
    expect(screen.getByText('This is the full conversation text that can be very long.')).toBeInTheDocument();
    expect(screen.getByText('87%')).toBeInTheDocument();
  });

  test('calls onClose when X button clicked', () => {
    vi.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
      data: mockChunkDetail,
      isLoading: false,
      isError: false,
    } as any);

    renderModal();
    fireEvent.click(screen.getByLabelText('Zavřít'));
    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });

  test('calls onClose on Escape key', () => {
    vi.spyOn(hooks, 'useChunkDetailQuery').mockReturnValue({
      data: mockChunkDetail,
      isLoading: false,
      isError: false,
    } as any);

    renderModal();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd frontend && npx vitest run src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx 2>&1 | tail -10
  ```
  Expected: fail — `ChunkDetailModal` module not found.

- [ ] **Step 3: Create the component**

  Create `frontend/src/components/knowledge-base/ChunkDetailModal.tsx`:

  ```typescript
  import React from 'react';
  import { X } from 'lucide-react';
  import { useChunkDetailQuery } from '../../api/hooks/useKnowledgeBase';
  import { formatDateTime } from '../../utils/formatters';

  interface ChunkDetailModalProps {
    chunkId: string;
    score?: number;
    onClose: () => void;
  }

  const ChunkDetailModal: React.FC<ChunkDetailModalProps> = ({ chunkId, score, onClose }) => {
    const { data, isLoading, isError } = useChunkDetailQuery(chunkId);

    React.useEffect(() => {
      const handleKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape') onClose();
      };
      document.addEventListener('keydown', handleKeyDown);
      return () => document.removeEventListener('keydown', handleKeyDown);
    }, [onClose]);

    return (
      <div
        role="dialog"
        aria-modal="true"
        className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"
      >
        <div className="bg-white rounded-lg shadow-xl w-[75vw] max-h-[90vh] overflow-hidden flex flex-col">
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0">
            <h2 className="text-base font-semibold text-gray-800 truncate">
              {data?.filename ?? 'Zdroj'}
            </h2>
            <button
              onClick={onClose}
              className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 flex-shrink-0"
              aria-label="Zavřít"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Body */}
          <div className="flex-1 overflow-y-auto p-6 space-y-5 text-sm">
            {isLoading && (
              <div className="space-y-3 animate-pulse">
                <div className="h-4 bg-gray-100 rounded w-1/3" />
                <div className="h-20 bg-gray-100 rounded" />
                <div className="h-4 bg-gray-100 rounded w-1/4" />
                <div className="h-40 bg-gray-100 rounded" />
              </div>
            )}

            {isError && (
              <p className="text-red-600">Zdroj se nepodařilo načíst.</p>
            )}

            {data && (
              <>
                {/* Meta row */}
                <div className="flex items-center gap-3 flex-wrap text-xs text-gray-500">
                  <span className="px-2 py-0.5 rounded-full bg-blue-100 text-blue-700 font-medium">
                    {data.documentType === 'Conversation' ? 'Konverzace' : 'Dokument'}
                  </span>
                  {data.indexedAt && (
                    <span>Indexováno: {formatDateTime(data.indexedAt)}</span>
                  )}
                  {score !== undefined && (
                    <span className="font-medium text-gray-700">
                      {Math.round(score * 100)}%
                    </span>
                  )}
                </div>

                {/* Summary */}
                <div>
                  <p className="text-xs text-gray-500 uppercase tracking-wide mb-2">Shrnutí</p>
                  <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3">
                    <p className="text-gray-800 whitespace-pre-wrap leading-relaxed">
                      {data.summary}
                    </p>
                  </div>
                </div>

                {/* Content */}
                <div>
                  <p className="text-xs text-gray-500 uppercase tracking-wide mb-2">Obsah</p>
                  <p className="text-gray-700 whitespace-pre-wrap leading-relaxed">
                    {data.content}
                  </p>
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    );
  };

  export default ChunkDetailModal;
  ```

- [ ] **Step 4: Run tests to verify they pass**

  ```bash
  cd frontend && npx vitest run src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx 2>&1 | tail -10
  ```
  Expected: `4 passed`.

- [ ] **Step 5: Commit**

  ```bash
  git add frontend/src/components/knowledge-base/ChunkDetailModal.tsx \
          frontend/src/components/knowledge-base/__tests__/ChunkDetailModal.test.tsx
  git commit -m "feat(kb): add ChunkDetailModal component"
  ```

---

### Task 8: Wire modal into `KnowledgeBaseAskTab`

**Files:**
- Modify: `frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx`

- [ ] **Step 1: Update `SourceAccordion` to accept a click handler**

  Replace the entire `KnowledgeBaseAskTab.tsx` content with:

  ```typescript
  import React, { useState } from 'react';
  import { MessageSquare, ChevronDown, ChevronUp, ExternalLink } from 'lucide-react';
  import {
    useKnowledgeBaseAskMutation,
    SourceReference,
  } from '../../api/hooks/useKnowledgeBase';
  import ChunkDetailModal from './ChunkDetailModal';

  interface SourceAccordionProps {
    sources: SourceReference[];
    onViewSource: (chunkId: string, score: number) => void;
  }

  const SourceAccordion: React.FC<SourceAccordionProps> = ({ sources, onViewSource }) => {
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
              <div key={src.chunkId} className="px-4 py-3 space-y-1">
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium text-gray-700">{src.filename}</span>
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-gray-400">
                      {Math.round(src.score * 100)}%
                    </span>
                    <button
                      onClick={() => onViewSource(src.chunkId, src.score)}
                      className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800"
                      aria-label={`Zobrazit zdroj ${src.filename}`}
                    >
                      <ExternalLink className="w-3 h-3" />
                      Zobrazit zdroj
                    </button>
                  </div>
                </div>
                <p className="text-xs text-gray-500 italic line-clamp-3">{src.excerpt}</p>
              </div>
            ))}
          </div>
        )}
      </div>
    );
  };

  const KnowledgeBaseAskTab: React.FC = () => {
    const [question, setQuestion] = useState('');
    const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);
    const [selectedScore, setSelectedScore] = useState<number | undefined>(undefined);
    const ask = useKnowledgeBaseAskMutation();

    const handleAsk = () => {
      if (question.trim()) {
        ask.mutate({ question: question.trim() });
      }
    };

    const handleViewSource = (chunkId: string, score: number) => {
      setSelectedChunkId(chunkId);
      setSelectedScore(score);
    };

    const handleCloseModal = () => {
      setSelectedChunkId(null);
      setSelectedScore(undefined);
    };

    return (
      <div className="space-y-4">
        <div className="space-y-2">
          <textarea
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder="Zadejte otázku k firemním dokumentům..."
            rows={3}
            className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
          />
          <button
            onClick={handleAsk}
            disabled={ask.isPending || !question.trim()}
            className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
          >
            <MessageSquare className="w-4 h-4" />
            Zeptat se
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
            <SourceAccordion sources={ask.data.sources} onViewSource={handleViewSource} />
          </div>
        )}

        {selectedChunkId && (
          <ChunkDetailModal
            chunkId={selectedChunkId}
            score={selectedScore}
            onClose={handleCloseModal}
          />
        )}
      </div>
    );
  };

  export default KnowledgeBaseAskTab;
  ```

- [ ] **Step 2: Build frontend to verify no type errors**

  ```bash
  cd frontend && npm run build 2>&1 | tail -10
  ```
  Expected: build succeeds.

- [ ] **Step 3: Commit**

  ```bash
  git add frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx
  git commit -m "feat(kb): wire ChunkDetailModal into Ask tab"
  ```

---

### Task 9: Wire modal into `KnowledgeBaseSearchTab`

**Files:**
- Modify: `frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx`

- [ ] **Step 1: Update `KnowledgeBaseSearchTab` to add modal trigger**

  Replace the entire `KnowledgeBaseSearchTab.tsx` content with:

  ```typescript
  import React, { useState } from 'react';
  import { Search, ExternalLink } from 'lucide-react';
  import {
    useKnowledgeBaseSearchMutation,
    ChunkResult,
  } from '../../api/hooks/useKnowledgeBase';
  import ChunkDetailModal from './ChunkDetailModal';

  const ScoreBadge: React.FC<{ score: number }> = ({ score }) => {
    const pct = Math.round(score * 100);
    const color =
      pct >= 80
        ? 'bg-green-100 text-green-800'
        : pct >= 60
        ? 'bg-yellow-100 text-yellow-800'
        : 'bg-gray-100 text-gray-600';
    return (
      <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${color}`}>
        {pct}%
      </span>
    );
  };

  interface ChunkCardProps {
    chunk: ChunkResult;
    onViewSource: (chunkId: string, score: number) => void;
  }

  const ChunkCard: React.FC<ChunkCardProps> = ({ chunk, onViewSource }) => (
    <div className="border border-gray-200 rounded-lg p-4 space-y-2">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-gray-700">{chunk.sourceFilename}</span>
        <div className="flex items-center gap-2">
          <ScoreBadge score={chunk.score} />
          <button
            onClick={() => onViewSource(chunk.chunkId, chunk.score)}
            className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-800"
            aria-label={`Zobrazit zdroj ${chunk.sourceFilename}`}
          >
            <ExternalLink className="w-3 h-3" />
            Zobrazit zdroj
          </button>
        </div>
      </div>
      <p className="text-sm text-gray-600 whitespace-pre-wrap line-clamp-5">{chunk.content}</p>
    </div>
  );

  const KnowledgeBaseSearchTab: React.FC = () => {
    const [query, setQuery] = useState('');
    const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);
    const [selectedScore, setSelectedScore] = useState<number | undefined>(undefined);
    const search = useKnowledgeBaseSearchMutation();

    const handleSearch = () => {
      if (query.trim()) {
        search.mutate({ query: query.trim() });
      }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') handleSearch();
    };

    const handleViewSource = (chunkId: string, score: number) => {
      setSelectedChunkId(chunkId);
      setSelectedScore(score);
    };

    const handleCloseModal = () => {
      setSelectedChunkId(null);
      setSelectedScore(undefined);
    };

    return (
      <div className="space-y-4">
        <div className="flex gap-2">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Hledat v znalostní bázi..."
            className="flex-1 border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            onClick={handleSearch}
            disabled={search.isPending || !query.trim()}
            className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
          >
            <Search className="w-4 h-4" />
            Hledat
          </button>
        </div>

        {search.isPending && (
          <div className="space-y-2 animate-pulse">
            {[1, 2, 3].map((i) => (
              <div key={i} className="h-24 bg-gray-100 rounded-lg" />
            ))}
          </div>
        )}

        {search.isError && (
          <div className="text-red-600 text-sm">Vyhledávání se nezdařilo. Zkuste to znovu.</div>
        )}

        {search.data && (
          <div className="space-y-3">
            {search.data.chunks.length === 0 ? (
              <p className="text-sm text-gray-500 text-center py-6">
                Žádné výsledky pro „{query}".
              </p>
            ) : (
              search.data.chunks.map((chunk) => (
                <ChunkCard key={chunk.chunkId} chunk={chunk} onViewSource={handleViewSource} />
              ))
            )}
          </div>
        )}

        {selectedChunkId && (
          <ChunkDetailModal
            chunkId={selectedChunkId}
            score={selectedScore}
            onClose={handleCloseModal}
          />
        )}
      </div>
    );
  };

  export default KnowledgeBaseSearchTab;
  ```

- [ ] **Step 2: Build frontend to verify no type errors**

  ```bash
  cd frontend && npm run build 2>&1 | tail -10
  ```
  Expected: build succeeds.

- [ ] **Step 3: Run full frontend test suite**

  ```bash
  cd frontend && npx vitest run 2>&1 | tail -15
  ```
  Expected: all tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx
  git commit -m "feat(kb): wire ChunkDetailModal into Search tab"
  ```

---

### Task 10: Add repository integration test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`

- [ ] **Step 1: Add integration test for `GetChunkByIdAsync`**

  Open `KnowledgeBaseRepositoryIntegrationTests.cs` and add a test (follow the existing pattern in that file for setup/teardown). Add:

  ```csharp
  [Fact]
  public async Task GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists()
  {
      // Use the existing test infrastructure from this file to:
      // 1. Insert a KnowledgeBaseDocument and a KnowledgeBaseChunk via the context
      // 2. Call repository.GetChunkByIdAsync(chunk.Id)
      // 3. Assert chunk is not null, chunk.Document is not null, chunk.Document.Filename matches
  }

  [Fact]
  public async Task GetChunkByIdAsync_ReturnsNull_WhenNotExists()
  {
      // Call repository.GetChunkByIdAsync(Guid.NewGuid())
      // Assert result is null
  }
  ```

  > **Note:** Look at the existing test setup in that file to match the database seeding pattern exactly. Use the same context/transaction handling that other tests in that file use.

- [ ] **Step 2: Run integration tests**

  ```bash
  cd backend && dotnet test --filter "KnowledgeBaseRepositoryIntegrationTests" -q
  ```
  Expected: new tests pass alongside existing ones.

- [ ] **Step 3: Commit**

  ```bash
  git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs
  git commit -m "test(kb): add GetChunkByIdAsync integration tests"
  ```

---

### Task 11: Final validation

- [ ] **Step 1: Run full backend test suite**

  ```bash
  cd backend && dotnet test -q
  ```
  Expected: all tests pass.

- [ ] **Step 2: Run full frontend test suite**

  ```bash
  cd frontend && npx vitest run 2>&1 | tail -15
  ```
  Expected: all tests pass.

- [ ] **Step 3: Build both projects**

  ```bash
  cd backend && dotnet build -q && cd ../frontend && npm run build 2>&1 | tail -5
  ```
  Expected: both succeed.

- [ ] **Step 4: Run dotnet format**

  ```bash
  cd backend && dotnet format --verify-no-changes
  ```
  Expected: no formatting violations. If there are, run `dotnet format` and commit the fix.
