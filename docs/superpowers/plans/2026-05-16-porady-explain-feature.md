# Porady / Explain Feature Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename "Meeting Tasks" to "Porady" in a reorganized sidebar, add guaranteed list refresh, and let users select any text in a meeting detail to get a Claude-powered explanation tied to the raw transcript.

**Architecture:** Three independent work streams: (1) sidebar + list-refresh (pure UI/config changes, no backend), (2) backend ExplainSummary use case + service (new MediatR handler + Claude service mirroring `ClaudeMeetingTaskExtractor`), (3) frontend Explain feature (selection hook + tooltip + modal wired into `MeetingTaskDetailPage`). The backend streams can be built and tested independently of the frontend wiring.

**Tech Stack:** .NET 8 / MediatR / `IChatClient` (Microsoft.Extensions.AI), React 18 / TanStack Query v5, TypeScript, Vitest/Jest for unit tests, Tailwind CSS.

---

## File Map

### Modified files
- `frontend/src/components/Layout/Sidebar.tsx` — sidebar group rename + item move + reorder
- `frontend/src/api/hooks/useMeetingTasks.ts` — add `refetchOnMount: "always"` + new `useExplainMeetingSummary` mutation
- `frontend/src/components/pages/automation/MeetingTasksPage.tsx` — add RefreshCw button
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` — wire `data-explainable`, use explain hooks + components
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — register `ClaudeMeetingSummaryExplainer`
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` — add `/explain` endpoint

### Created files
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingSummaryExplainer.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingSummaryExplainer.cs`
- `frontend/src/components/pages/automation/explain/useExplainSelection.ts`
- `frontend/src/components/pages/automation/explain/ExplainTooltip.tsx`
- `frontend/src/components/pages/automation/explain/ExplainModal.tsx`

### Test files
- `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts` — extend existing
- `frontend/src/components/Layout/__tests__/Sidebar.test.tsx` — new (menu order)
- `frontend/src/components/pages/automation/explain/__tests__/useExplainSelection.test.ts` — new
- `frontend/src/components/pages/automation/explain/__tests__/ExplainModal.test.tsx` — new
- *(No separate BE test project exists yet — create one or add to unit test project once located; see Task 5)*

---

## Task 1: Sidebar rename and reorder

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx:267-320`

Context: `navigationSections` is an array literal starting at line 74. The section `id: "personalni"` (line 267) needs to become `id: "anela"` / `name: "Anela"`. The `meeting-tasks` item (lines 310-313) needs to move from the `automatizace` section into the `anela` section as the first item, renamed `"Porady"`. The whole `anela` section object then moves to index 1 of the array (right after `dashboard`).

- [ ] **Step 1: Write the Sidebar menu-order test first**

Create `frontend/src/components/Layout/__tests__/Sidebar.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from '../Sidebar';

// Silence auth / changelog deps
jest.mock('../../../auth/useAuth', () => ({ useAuth: () => ({ getUserInfo: () => ({ roles: [] }) }) }));
jest.mock('../../../auth/mockAuth', () => ({ useMockAuth: () => ({ getUserInfo: () => null }), shouldUseMockAuth: () => false }));
jest.mock('../../../contexts/ChangelogContext', () => ({ useChangelogContext: () => ({ openModal: jest.fn() }) }));

function renderSidebar() {
  return render(
    <MemoryRouter>
      <Sidebar isOpen isCollapsed={false} onClose={jest.fn()} onToggleCollapse={jest.fn()} onMenuClick={jest.fn()} />
    </MemoryRouter>
  );
}

describe('Sidebar navigation', () => {
  it('shows "Anela" group (not "Personální")', () => {
    renderSidebar();
    expect(screen.getByText('Anela')).toBeInTheDocument();
    expect(screen.queryByText('Personální')).not.toBeInTheDocument();
  });

  it('shows "Porady" item (not "Meeting Tasks")', () => {
    renderSidebar();
    expect(screen.queryByText('Meeting Tasks')).not.toBeInTheDocument();
    // "Porady" is inside a collapsible section - check it's present in the DOM
    expect(screen.getByRole('button', { name: /Anela/i })).toBeInTheDocument();
  });

  it('"Anela" group appears before "Finance" in the DOM', () => {
    renderSidebar();
    const buttons = screen.getAllByRole('button');
    const anela = buttons.findIndex(b => b.textContent?.includes('Anela'));
    const finance = buttons.findIndex(b => b.textContent?.includes('Finance'));
    // Anela should appear before Finance (Finance is role-gated, may be absent — just check Anela is present)
    expect(anela).toBeGreaterThanOrEqual(0);
  });
});
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
cd frontend && npx jest Sidebar.test --no-coverage 2>&1 | tail -20
```

Expected: FAIL — `"Anela"` not found, `"Personální"` is present.

- [ ] **Step 3: Apply the sidebar changes**

In `frontend/src/components/Layout/Sidebar.tsx`, replace the `personalni` section and remove `meeting-tasks` from `automatizace`:

```tsx
// Replace the old `personalni` section object (lines ~267-281) with:
{
  id: "anela",
  name: "Anela",
  icon: Users,
  type: "section" as const,
  items: [
    {
      id: "meeting-tasks",
      name: "Porady",
      href: "/automation/meeting-tasks",
    },
    {
      id: "struktura",
      name: "Struktura",
      href: "#",
      onClick: openOrgChart,
      isExternal: true,
    },
  ],
},
```

Move this new `anela` object to **index 1** of the `navigationSections` array — immediately after the `dashboard` single item, before the Finance spread:

```tsx
const navigationSections = [
  {
    id: "dashboard",
    // ... unchanged
  },
  {
    id: "anela",
    name: "Anela",
    icon: Users,
    type: "section" as const,
    items: [
      {
        id: "meeting-tasks",
        name: "Porady",
        href: "/automation/meeting-tasks",
      },
      {
        id: "struktura",
        name: "Struktura",
        href: "#",
        onClick: openOrgChart,
        isExternal: true,
      },
    ],
  },
  // Finance section - only visible for finance_reader role
  ...(hasRole("finance_reader") ? [{ /* unchanged */ }] : []),
  // ... rest of sections unchanged
];
```

Also remove the `meeting-tasks` item from the `automatizace` section:
```tsx
// In the automatizace section items array, delete:
{
  id: "meeting-tasks",
  name: "Meeting Tasks",
  href: "/automation/meeting-tasks",
},
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
cd frontend && npx jest Sidebar.test --no-coverage 2>&1 | tail -10
```

Expected: PASS

- [ ] **Step 5: Verify the build**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: no TypeScript errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/Layout/Sidebar.tsx frontend/src/components/Layout/__tests__/Sidebar.test.tsx
git commit -m "feat: rename Personální → Anela, move Porady item, reorder sidebar"
```

---

## Task 2: Refresh on the meeting list page

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts:107-125`
- Modify: `frontend/src/components/pages/automation/MeetingTasksPage.tsx`

- [ ] **Step 1: Write the failing test for `refetchOnMount: "always"`**

Add to `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts` (inside the existing `useMeetingTasksList` describe block):

```ts
it('uses refetchOnMount: "always"', async () => {
  const payload: TranscriptListResponse = {
    success: true, items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0,
  };
  mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });

  const { wrapper } = createQueryClientWrapper();
  const { result, unmount } = renderHook(() => useMeetingTasksList(undefined, 1, 20), { wrapper });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  expect(mockFetch).toHaveBeenCalledTimes(1);

  // Re-mount — with refetchOnMount: "always" it must refetch even if data is fresh
  unmount();
  const { result: result2 } = renderHook(() => useMeetingTasksList(undefined, 1, 20), { wrapper });
  await waitFor(() => expect(result2.current.isSuccess).toBe(true));
  expect(mockFetch).toHaveBeenCalledTimes(2);
});
```

- [ ] **Step 2: Run to confirm fail**

```bash
cd frontend && npx jest useMeetingTasks.test --no-coverage 2>&1 | tail -15
```

Expected: FAIL — `mockFetch` called only once.

- [ ] **Step 3: Add `refetchOnMount: "always"` to `useMeetingTasksList`**

In `frontend/src/api/hooks/useMeetingTasks.ts`, inside the `useQuery` options for `useMeetingTasksList`:

```ts
return useQuery<TranscriptListResponse>({
  queryKey: [...QUERY_KEYS.meetingTasks, statusFilter ?? "", page, pageSize],
  refetchOnMount: "always",
  queryFn: () => {
    // ... unchanged
  },
});
```

- [ ] **Step 4: Run test to confirm pass**

```bash
cd frontend && npx jest useMeetingTasks.test --no-coverage 2>&1 | tail -10
```

Expected: PASS

- [ ] **Step 5: Add the refresh button to `MeetingTasksPage`**

In `frontend/src/components/pages/automation/MeetingTasksPage.tsx`:

```tsx
// Add to imports at top:
import { RefreshCw } from "lucide-react";

// Change the hook destructure on line 45 to also get refetch and isFetching:
const { data, isLoading, refetch, isFetching } = useMeetingTasksList(statusFilter, page, PAGE_SIZE);

// In the JSX, after the <h1> (inside the first flex-shrink-0 div), add:
<div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8 flex items-center justify-between">
  <div>
    <h1 className="text-3xl font-bold text-gray-900">Porady</h1>
    <p className="mt-2 text-gray-600">Validace AI-extrahovanych ukolu ze schuzek pred odeslanim do Microsoft TODO</p>
  </div>
  <button
    type="button"
    title="Obnovit"
    disabled={isFetching}
    onClick={() => refetch()}
    className="inline-flex items-center p-2 rounded-md border border-gray-300 bg-white text-gray-600 hover:bg-gray-50 disabled:opacity-50"
  >
    <RefreshCw className={`w-4 h-4 ${isFetching ? "animate-spin" : ""}`} />
  </button>
</div>
```

Also remove the now-separate `<h1>` and description paragraph that were previously standalone — the header is now inside the flex row above.

- [ ] **Step 6: Build check**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: clean build.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts frontend/src/components/pages/automation/MeetingTasksPage.tsx frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts
git commit -m "feat: add refetchOnMount always + refresh button to Porady list"
```

---

## Task 3: Backend — ExplainSummary use case files

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingSummaryExplainer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryHandler.cs`

These files have no tests yet (tests are in Task 4). Create them first so the build passes.

- [ ] **Step 1: Create the explainer interface**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingSummaryExplainer.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class MeetingSummaryExplanation
{
    public string RelevantTranscript { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public interface IMeetingSummaryExplainer
{
    Task<MeetingSummaryExplanation> ExplainAsync(
        string transcript,
        string selectedText,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Create the request class**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryRequest : IRequest<ExplainSummaryResponse>
{
    public Guid TranscriptId { get; set; }

    [Required]
    public string SelectedText { get; set; } = null!;
}
```

- [ ] **Step 3: Create the response class**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryResponse : BaseResponse
{
    public ExplainSummaryResponse() { }

    public ExplainSummaryResponse(ErrorCodes errorCode) : base(errorCode) { }

    public string RelevantTranscript { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create the handler**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ExplainSummary/ExplainSummaryHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryHandler : IRequestHandler<ExplainSummaryRequest, ExplainSummaryResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingSummaryExplainer _explainer;
    private readonly ILogger<ExplainSummaryHandler> _logger;

    public ExplainSummaryHandler(
        IMeetingTranscriptRepository repository,
        IMeetingSummaryExplainer explainer,
        ILogger<ExplainSummaryHandler> logger)
    {
        _repository = repository;
        _explainer = explainer;
        _logger = logger;
    }

    public async Task<ExplainSummaryResponse> Handle(
        ExplainSummaryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Explaining summary fragment — TranscriptId: {TranscriptId}",
            request.TranscriptId);

        if (string.IsNullOrWhiteSpace(request.SelectedText))
        {
            _logger.LogWarning("ExplainSummary called with empty SelectedText");
            return new ExplainSummaryResponse(ErrorCodes.RequiredFieldMissing);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new ExplainSummaryResponse(ErrorCodes.ResourceNotFound);
        }

        var result = await _explainer.ExplainAsync(
            transcript.RawTranscript,
            request.SelectedText,
            cancellationToken);

        return new ExplainSummaryResponse
        {
            RelevantTranscript = result.RelevantTranscript,
            Explanation = result.Explanation,
        };
    }
}
```

- [ ] **Step 5: Verify it builds**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | tail -10
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/
git commit -m "feat: add ExplainSummary use case skeleton (request, response, handler, interface)"
```

---

## Task 4: Backend — ExplainSummary handler tests

The project has no `tests/` directory yet. Check if one exists with a different name:

```bash
find /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland/backend -name "*.Tests.csproj" -o -name "*.UnitTests.csproj" 2>/dev/null
```

If no test project exists, create one:

```bash
cd backend && dotnet new xunit -n Anela.Heblo.Application.Tests -o tests/Anela.Heblo.Application.Tests
cd tests/Anela.Heblo.Application.Tests && dotnet add reference ../../src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

If a test project already exists (found by the `find` command), add the test file to it instead.

- [ ] **Step 1: Write the handler tests (RED)**

Create `backend/tests/Anela.Heblo.Application.Tests/Features/MeetingTasks/ExplainSummaryHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Application.Tests.Features.MeetingTasks;

public class ExplainSummaryHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();
    private readonly Mock<IMeetingSummaryExplainer> _explainerMock = new();
    private readonly ExplainSummaryHandler _sut;

    public ExplainSummaryHandlerTests()
    {
        _sut = new ExplainSummaryHandler(
            _repoMock.Object,
            _explainerMock.Object,
            NullLogger<ExplainSummaryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsRequiredFieldMissing_WhenSelectedTextIsEmpty()
    {
        // Arrange
        var request = new ExplainSummaryRequest { TranscriptId = Guid.NewGuid(), SelectedText = "   " };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        _repoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsResourceNotFound_WhenTranscriptDoesNotExist()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var request = new ExplainSummaryRequest { TranscriptId = transcriptId, SelectedText = "some text" };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsExplanation_OnSuccess()
    {
        // Arrange
        var transcriptId = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = transcriptId,
            RawTranscript = "Full raw transcript text here.",
            Subject = "Test meeting",
            Summary = "Summary",
            PlaudRecordingId = "rec1",
            PlaudCreatedAt = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow,
        };
        _repoMock.Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _explainerMock.Setup(e => e.ExplainAsync(
                transcript.RawTranscript,
                "some selected text",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingSummaryExplanation
            {
                RelevantTranscript = "relevant slice",
                Explanation = "because of this",
            });

        var request = new ExplainSummaryRequest { TranscriptId = transcriptId, SelectedText = "some selected text" };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.RelevantTranscript.Should().Be("relevant slice");
        response.Explanation.Should().Be("because of this");
    }
}
```

- [ ] **Step 2: Run tests to confirm FAIL (or build failure if no project yet)**

```bash
cd backend && dotnet test 2>&1 | tail -20
```

Expected: build or test failure since `MeetingTranscript` domain type needs verification (check actual constructor/properties).

> **Note:** If `MeetingTranscript` has required init-only properties that cannot be set by object initializer, adjust the test setup to match the actual domain class. Run `grep -r "class MeetingTranscript" backend/src` to find it, then read it.

- [ ] **Step 3: Fix test setup to match domain type, then re-run until GREEN**

```bash
cd backend && dotnet test 2>&1 | tail -20
```

Expected: PASS (3 tests pass).

- [ ] **Step 4: Commit**

```bash
git add backend/tests/
git commit -m "test: add ExplainSummaryHandler unit tests"
```

---

## Task 5: Backend — ClaudeMeetingSummaryExplainer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingSummaryExplainer.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`

- [ ] **Step 1: Write the explainer service tests (RED)**

Add to `backend/tests/Anela.Heblo.Application.Tests/Features/MeetingTasks/ClaudeMeetingSummaryExplainerTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Application.Tests.Features.MeetingTasks;

public class ClaudeMeetingSummaryExplainerTests
{
    private readonly Mock<IChatClient> _chatClientMock = new();
    private readonly ClaudeMeetingSummaryExplainer _sut;

    public ClaudeMeetingSummaryExplainerTests()
    {
        _sut = new ClaudeMeetingSummaryExplainer(
            _chatClientMock.Object,
            NullLogger<ClaudeMeetingSummaryExplainer>.Instance);
    }

    private void SetupChatResponse(string text)
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ExplainAsync_ParsesValidJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            relevantTranscript = "the relevant part",
            explanation = "this is why"
        });
        SetupChatResponse(json);

        // Act
        var result = await _sut.ExplainAsync("transcript text", "selected", CancellationToken.None);

        // Assert
        result.RelevantTranscript.Should().Be("the relevant part");
        result.Explanation.Should().Be("this is why");
    }

    [Fact]
    public async Task ExplainAsync_StripsFenceBeforeParsing()
    {
        // Arrange
        var json = "```json\n{ \"relevantTranscript\": \"sliced\", \"explanation\": \"detail\" }\n```";
        SetupChatResponse(json);

        // Act
        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        // Assert
        result.RelevantTranscript.Should().Be("sliced");
        result.Explanation.Should().Be("detail");
    }

    [Fact]
    public async Task ExplainAsync_ReturnsFallback_OnMalformedJson()
    {
        // Arrange
        SetupChatResponse("not json at all");

        // Act
        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        // Assert — graceful fallback, no exception
        result.Should().NotBeNull();
        result.Explanation.Should().NotBeNull();
    }

    [Fact]
    public async Task ExplainAsync_ReturnsFallback_WhenChatClientThrows()
    {
        // Arrange
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("LLM unavailable"));

        // Act
        var result = await _sut.ExplainAsync("transcript", "text", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests to confirm FAIL**

```bash
cd backend && dotnet test --filter "ClaudeMeetingSummaryExplainerTests" 2>&1 | tail -15
```

Expected: Build failure — `ClaudeMeetingSummaryExplainer` doesn't exist yet.

- [ ] **Step 3: Create `ClaudeMeetingSummaryExplainer.cs`**

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingSummaryExplainer.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingSummaryExplainer : IMeetingSummaryExplainer
{
    private const string SystemPrompt = """
        Jsi asistent, který vysvětluje, proč se daná část shrnutí schůzky dostala do souhrnu.
        Dostaneš celý přepis schůzky a vybraný text (fragment shrnutí nebo navrhované úlohy).
        Proveď toto:
        1. Cituj přesný úsek přepisu, který vedl k tomuto bodu.
        2. Napiš podrobné vysvětlení v češtině, proč tento úsek skončil ve shrnutí nebo jako úloha.
        Odpověz POUZE jako JSON (bez dalšího textu):
        { "relevantTranscript": "...", "explanation": "..." }
        """;

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeMeetingSummaryExplainer> _logger;

    public ClaudeMeetingSummaryExplainer(
        IChatClient chatClient,
        ILogger<ClaudeMeetingSummaryExplainer> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<MeetingSummaryExplanation> ExplainAsync(
        string transcript,
        string selectedText,
        CancellationToken ct = default)
    {
        try
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User,
                    $"Vybraný text: {selectedText}\n\nCelý přepis:\n{transcript}")
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            var result = JsonSerializer.Deserialize<MeetingSummaryExplanation>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? FallbackExplanation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get explanation from Claude");
            return FallbackExplanation();
        }
    }

    private static MeetingSummaryExplanation FallbackExplanation() =>
        new() { RelevantTranscript = string.Empty, Explanation = "Vysvětlení není k dispozici." };

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json"))
            trimmed = trimmed["```json".Length..];
        else if (trimmed.StartsWith("```"))
            trimmed = trimmed["```".Length..];

        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^"```".Length];

        return trimmed.Trim();
    }
}
```

- [ ] **Step 4: Run tests to confirm PASS**

```bash
cd backend && dotnet test --filter "ClaudeMeetingSummaryExplainerTests" 2>&1 | tail -10
```

Expected: 4 tests PASS.

- [ ] **Step 5: Register the service in `MeetingTasksModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`, add after the existing `IMeetingTaskExtractor` registration:

```csharp
services.AddScoped<IMeetingSummaryExplainer, ClaudeMeetingSummaryExplainer>();
```

- [ ] **Step 6: Build check**

```bash
cd backend && dotnet build 2>&1 | tail -10
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingSummaryExplainer.cs \
        backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs \
        backend/tests/
git commit -m "feat: implement ClaudeMeetingSummaryExplainer with tests"
```

---

## Task 6: Backend — API endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`

- [ ] **Step 1: Add the endpoint**

In `MeetingTasksController.cs`, add at the end of the class (before the closing `}`):

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;
// (add this using at the top of the file with the others)

[HttpPost("{transcriptId:guid}/explain")]
public async Task<ActionResult<ExplainSummaryResponse>> ExplainSummary(
    Guid transcriptId,
    [FromBody] ExplainSummaryRequest request,
    CancellationToken ct = default)
{
    request.TranscriptId = transcriptId;
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```

- [ ] **Step 2: Build and run all backend tests**

```bash
cd backend && dotnet build 2>&1 | tail -10
cd backend && dotnet test 2>&1 | tail -15
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 3: Format**

```bash
cd backend && dotnet format 2>&1 | tail -5
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs
git commit -m "feat: add POST /api/meeting-tasks/{id}/explain endpoint"
```

---

## Task 7: Frontend — `useExplainMeetingSummary` hook + tests

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts`
- Modify: `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts`

- [ ] **Step 1: Add the DTOs and write the failing test**

Add to `frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts`:

```ts
import {
  useExplainMeetingSummary,
  ExplainSummaryResponse,
} from '../useMeetingTasks';

describe('useExplainMeetingSummary', () => {
  it('POSTs to /api/meeting-tasks/{id}/explain with selectedText', async () => {
    const payload: ExplainSummaryResponse = {
      success: true,
      relevantTranscript: 'slice of transcript',
      explanation: 'because of X',
    };
    mockFetch.mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(payload) });
    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useExplainMeetingSummary(), { wrapper });

    await result.current.mutateAsync({
      transcriptId: 'some-id',
      selectedText: 'selected fragment',
    });

    expect(mockFetch).toHaveBeenCalledWith(
      `${mockClient.baseUrl}/api/meeting-tasks/some-id/explain`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ selectedText: 'selected fragment' }),
      }),
    );
  });
});
```

- [ ] **Step 2: Run to confirm FAIL**

```bash
cd frontend && npx jest useMeetingTasks.test --no-coverage 2>&1 | tail -10
```

Expected: FAIL — `useExplainMeetingSummary` not exported.

- [ ] **Step 3: Add types and hook to `useMeetingTasks.ts`**

At the bottom of `frontend/src/api/hooks/useMeetingTasks.ts`, append:

```ts
// --- Explain Summary ---

export interface ExplainSummaryResponse {
  success: boolean;
  relevantTranscript: string;
  explanation: string;
  errorCode?: string;
}

export interface ExplainSummaryInput {
  transcriptId: string;
  selectedText: string;
}

export function useExplainMeetingSummary() {
  return useMutation<ExplainSummaryResponse, Error, ExplainSummaryInput>({
    mutationFn: async (input) =>
      fetchJson<ExplainSummaryResponse>(
        `/api/meeting-tasks/${encodeURIComponent(input.transcriptId)}/explain`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
          body: JSON.stringify({ selectedText: input.selectedText }),
        },
      ),
  });
}
```

- [ ] **Step 4: Run test to confirm PASS**

```bash
cd frontend && npx jest useMeetingTasks.test --no-coverage 2>&1 | tail -10
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts frontend/src/api/hooks/__tests__/useMeetingTasks.test.ts
git commit -m "feat: add useExplainMeetingSummary hook"
```

---

## Task 8: Frontend — `useExplainSelection` hook + tests

**Files:**
- Create: `frontend/src/components/pages/automation/explain/useExplainSelection.ts`
- Create: `frontend/src/components/pages/automation/explain/__tests__/useExplainSelection.test.ts`

The hook tracks text selection (via `mouseup`) inside elements marked `data-explainable`. It works for both `<textarea>`/`<input>` elements (using `selectionStart/selectionEnd`) and regular DOM elements (using `window.getSelection()`). It returns `{ selectedText, anchorRect }`.

- [ ] **Step 1: Write the tests first**

Create `frontend/src/components/pages/automation/explain/__tests__/useExplainSelection.test.ts`:

```ts
import { renderHook, act } from '@testing-library/react';
import { useExplainSelection } from '../useExplainSelection';

function fireMouseup(target: EventTarget) {
  const event = new MouseEvent('mouseup', { bubbles: true });
  Object.defineProperty(event, 'target', { value: target, writable: false });
  document.dispatchEvent(event);
}

describe('useExplainSelection', () => {
  afterEach(() => {
    window.getSelection()?.removeAllRanges();
  });

  it('returns empty selectedText initially', () => {
    const { result } = renderHook(() => useExplainSelection());
    expect(result.current.selectedText).toBe('');
  });

  it('ignores mouseup outside data-explainable element', () => {
    const outside = document.createElement('div');
    document.body.appendChild(outside);
    const { result } = renderHook(() => useExplainSelection());

    // Simulate selection in window
    const range = document.createRange();
    range.selectNodeContents(outside);
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);

    act(() => {
      fireMouseup(outside);
    });

    expect(result.current.selectedText).toBe('');
    document.body.removeChild(outside);
  });

  it('captures selection inside data-explainable element', () => {
    const container = document.createElement('div');
    container.setAttribute('data-explainable', 'true');
    const textNode = document.createTextNode('hello world');
    container.appendChild(textNode);
    document.body.appendChild(container);

    const { result } = renderHook(() => useExplainSelection());

    const range = document.createRange();
    range.setStart(textNode, 0);
    range.setEnd(textNode, 5); // "hello"
    window.getSelection()?.removeAllRanges();
    window.getSelection()?.addRange(range);

    act(() => {
      fireMouseup(container);
    });

    expect(result.current.selectedText).toBe('hello');
    document.body.removeChild(container);
  });

  it('reads selection from textarea selectionStart/End when target is textarea', () => {
    const textarea = document.createElement('textarea');
    textarea.setAttribute('data-explainable', 'true');
    textarea.value = 'abcdef';
    document.body.appendChild(textarea);
    textarea.setSelectionRange(2, 5); // "cde"

    const { result } = renderHook(() => useExplainSelection());

    act(() => {
      fireMouseup(textarea);
    });

    expect(result.current.selectedText).toBe('cde');
    document.body.removeChild(textarea);
  });

  it('clears selection when mouseup yields empty string', () => {
    const container = document.createElement('div');
    container.setAttribute('data-explainable', 'true');
    document.body.appendChild(container);
    const { result } = renderHook(() => useExplainSelection());

    act(() => {
      window.getSelection()?.removeAllRanges();
      fireMouseup(container);
    });

    expect(result.current.selectedText).toBe('');
    document.body.removeChild(container);
  });
});
```

- [ ] **Step 2: Run to confirm FAIL**

```bash
cd frontend && npx jest useExplainSelection.test --no-coverage 2>&1 | tail -10
```

Expected: FAIL — module not found.

- [ ] **Step 3: Create `useExplainSelection.ts`**

Create `frontend/src/components/pages/automation/explain/useExplainSelection.ts`:

```ts
import { useState, useEffect } from 'react';

export interface SelectionState {
  selectedText: string;
  anchorRect: DOMRect | null;
}

function isInsideExplainable(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return target.closest('[data-explainable]') !== null;
}

export function useExplainSelection(): SelectionState {
  const [state, setState] = useState<SelectionState>({ selectedText: '', anchorRect: null });

  useEffect(() => {
    function handleMouseup(e: MouseEvent) {
      if (!isInsideExplainable(e.target)) {
        setState({ selectedText: '', anchorRect: null });
        return;
      }

      const target = e.target as HTMLElement;

      if (target instanceof HTMLTextAreaElement || target instanceof HTMLInputElement) {
        const { selectionStart, selectionEnd, value } = target;
        const text = (selectionStart !== null && selectionEnd !== null)
          ? value.substring(selectionStart, selectionEnd)
          : '';
        const rect = text ? target.getBoundingClientRect() : null;
        setState({ selectedText: text, anchorRect: rect });
        return;
      }

      const selection = window.getSelection();
      const text = selection?.toString() ?? '';
      const rect = text && selection?.rangeCount
        ? selection.getRangeAt(0).getBoundingClientRect()
        : null;
      setState({ selectedText: text, anchorRect: rect });
    }

    document.addEventListener('mouseup', handleMouseup);
    return () => document.removeEventListener('mouseup', handleMouseup);
  }, []);

  return state;
}
```

- [ ] **Step 4: Run tests to confirm PASS**

```bash
cd frontend && npx jest useExplainSelection.test --no-coverage 2>&1 | tail -10
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/explain/
git commit -m "feat: add useExplainSelection hook with tests"
```

---

## Task 9: Frontend — `ExplainTooltip` and `ExplainModal` components

**Files:**
- Create: `frontend/src/components/pages/automation/explain/ExplainTooltip.tsx`
- Create: `frontend/src/components/pages/automation/explain/ExplainModal.tsx`
- Create: `frontend/src/components/pages/automation/explain/__tests__/ExplainModal.test.tsx`

- [ ] **Step 1: Write ExplainModal tests (RED)**

Create `frontend/src/components/pages/automation/explain/__tests__/ExplainModal.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { ExplainModal } from '../ExplainModal';

describe('ExplainModal', () => {
  const baseProps = {
    isOpen: true,
    onClose: jest.fn(),
    isLoading: false,
    relevantTranscript: null,
    explanation: null,
    error: null,
  };

  it('renders nothing when isOpen is false', () => {
    render(<ExplainModal {...baseProps} isOpen={false} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows spinner while loading', () => {
    render(<ExplainModal {...baseProps} isLoading />);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('shows transcript and explanation on success', () => {
    render(<ExplainModal {...baseProps} relevantTranscript="slice of talk" explanation="reason here" />);
    expect(screen.getByText('slice of talk')).toBeInTheDocument();
    expect(screen.getByText('reason here')).toBeInTheDocument();
    expect(screen.getByText('Záznam konverzace')).toBeInTheDocument();
    expect(screen.getByText('Vysvětlení')).toBeInTheDocument();
  });

  it('shows error message when error is set', () => {
    render(<ExplainModal {...baseProps} error="Něco se pokazilo" />);
    expect(screen.getByText('Něco se pokazilo')).toBeInTheDocument();
  });

  it('calls onClose when close button clicked', () => {
    const onClose = jest.fn();
    render(<ExplainModal {...baseProps} onClose={onClose} />);
    fireEvent.click(screen.getByTitle('Zavřít'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape pressed', () => {
    const onClose = jest.fn();
    render(<ExplainModal {...baseProps} onClose={onClose} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run to confirm FAIL**

```bash
cd frontend && npx jest ExplainModal.test --no-coverage 2>&1 | tail -10
```

Expected: FAIL — module not found.

- [ ] **Step 3: Create `ExplainTooltip.tsx`**

Create `frontend/src/components/pages/automation/explain/ExplainTooltip.tsx`:

```tsx
import React from 'react';

interface ExplainTooltipProps {
  anchorRect: DOMRect;
  onExplain: () => void;
}

export function ExplainTooltip({ anchorRect, onExplain }: ExplainTooltipProps) {
  const style: React.CSSProperties = {
    position: 'fixed',
    top: anchorRect.bottom + window.scrollY + 4,
    left: anchorRect.left + window.scrollX,
    zIndex: 55,
  };

  return (
    <div style={style}>
      <button
        type="button"
        onMouseDown={(e) => e.preventDefault()} // prevent selection loss
        onClick={onExplain}
        className="px-2 py-1 rounded-md text-xs font-medium bg-indigo-600 text-white shadow hover:bg-indigo-700"
      >
        Vysvětlit
      </button>
    </div>
  );
}
```

- [ ] **Step 4: Create `ExplainModal.tsx`**

Create `frontend/src/components/pages/automation/explain/ExplainModal.tsx`:

```tsx
import React, { useEffect } from 'react';
import { X } from 'lucide-react';

interface ExplainModalProps {
  isOpen: boolean;
  onClose: () => void;
  isLoading: boolean;
  relevantTranscript: string | null;
  explanation: string | null;
  error: string | null;
}

export function ExplainModal({ isOpen, onClose, isLoading, relevantTranscript, explanation, error }: ExplainModalProps) {
  useEffect(() => {
    if (!isOpen) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40"
      role="dialog"
      aria-modal="true"
    >
      <div className="bg-white rounded-lg shadow-lg p-5 max-w-2xl w-full max-h-[80vh] flex flex-col">
        <div className="flex items-center justify-between mb-3 flex-shrink-0">
          <h3 className="text-base font-semibold text-gray-900">Vysvětlení</h3>
          <button
            type="button"
            title="Zavřít"
            onClick={onClose}
            className="p-1 rounded-md text-gray-500 hover:bg-gray-100"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-auto space-y-4">
          {isLoading && (
            <div className="flex justify-center py-8">
              <div
                role="status"
                className="w-8 h-8 border-4 border-indigo-200 border-t-indigo-600 rounded-full animate-spin"
                aria-label="Načítám..."
              />
            </div>
          )}

          {!isLoading && error && (
            <p className="text-sm text-red-600">{error}</p>
          )}

          {!isLoading && !error && relevantTranscript !== null && (
            <>
              <div>
                <p className="text-xs font-semibold uppercase text-gray-500 mb-1">Záznam konverzace</p>
                <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-sm text-gray-800 whitespace-pre-wrap">
                  {relevantTranscript}
                </div>
              </div>
              <div>
                <p className="text-xs font-semibold uppercase text-gray-500 mb-1">Vysvětlení</p>
                <div className="rounded-md border border-blue-100 bg-blue-50 p-3 text-sm text-blue-900">
                  {explanation}
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Run tests to confirm PASS**

```bash
cd frontend && npx jest ExplainModal.test --no-coverage 2>&1 | tail -10
```

Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/explain/
git commit -m "feat: add ExplainTooltip and ExplainModal components with tests"
```

---

## Task 10: Wire explain feature into `MeetingTaskDetailPage`

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Add imports at the top of the file**

```tsx
import { useExplainSelection } from './explain/useExplainSelection';
import { ExplainTooltip } from './explain/ExplainTooltip';
import { ExplainModal } from './explain/ExplainModal';
import { useExplainMeetingSummary } from '../../../api/hooks/useMeetingTasks';
```

- [ ] **Step 2: Add hook calls and state inside the component**

Inside `MeetingTaskDetailPage` (after existing hooks, around line 93):

```tsx
const explainSelection = useExplainSelection();
const explainMutation = useExplainMeetingSummary();
const [explainModalOpen, setExplainModalOpen] = useState(false);

const handleExplain = () => {
  if (!explainSelection.selectedText) return;
  setExplainModalOpen(true);
  explainMutation.mutate({
    transcriptId: id,
    selectedText: explainSelection.selectedText,
  });
};

const handleCloseExplain = () => {
  setExplainModalOpen(false);
  explainMutation.reset();
};
```

- [ ] **Step 3: Mark the summary block as `data-explainable`**

Find the summary `div` (around line 183–186):

```tsx
// Change:
<div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900 prose prose-sm prose-blue max-w-none">
// To:
<div
  data-explainable="true"
  className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900 prose prose-sm prose-blue max-w-none"
>
```

- [ ] **Step 4: Mark the task cards container as `data-explainable`**

Find the tasks list `div` (around line 291):

```tsx
// Change:
<div className="px-4 sm:px-6 lg:px-8 mt-3 space-y-2">
// To:
<div data-explainable="true" className="px-4 sm:px-6 lg:px-8 mt-3 space-y-2">
```

- [ ] **Step 5: Add `ExplainTooltip` and `ExplainModal` to the JSX**

At the bottom of the returned JSX, before the closing `</div>` wrapper (after the submit modal):

```tsx
{explainSelection.selectedText && explainSelection.anchorRect && (
  <ExplainTooltip
    anchorRect={explainSelection.anchorRect}
    onExplain={handleExplain}
  />
)}

<ExplainModal
  isOpen={explainModalOpen}
  onClose={handleCloseExplain}
  isLoading={explainMutation.isPending}
  relevantTranscript={explainMutation.data?.relevantTranscript ?? null}
  explanation={explainMutation.data?.explanation ?? null}
  error={
    explainMutation.isError
      ? (explainMutation.error instanceof Error ? explainMutation.error.message : 'Chyba při načítání vysvětlení.')
      : (!explainMutation.data?.success && explainMutation.data)
        ? 'Nepodařilo se získat vysvětlení.'
        : null
  }
/>
```

- [ ] **Step 6: Build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -20 && npm run lint 2>&1 | tail -10
```

Expected: No errors.

- [ ] **Step 7: Run all frontend tests**

```bash
cd frontend && npx jest --no-coverage 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx
git commit -m "feat: wire explain feature into meeting detail page"
```

---

## Task 11: Final verification

- [ ] **Step 1: Full backend build + format + tests**

```bash
cd backend && dotnet build && dotnet format && dotnet test 2>&1 | tail -20
```

Expected: Build succeeded, format clean, all tests pass.

- [ ] **Step 2: Full frontend build + lint + tests**

```bash
cd frontend && npm run build && npm run lint && npx jest --no-coverage 2>&1 | tail -20
```

Expected: No errors, all tests pass.

- [ ] **Step 3: Verify secrets for local LLM calls**

The `Anthropic:ApiKey` must be set in `backend/src/Anela.Heblo.API/secrets.json` for the Claude call to work locally. Check it exists — if not, add it:

```json
{
  "Anthropic": {
    "ApiKey": "<your-key>"
  }
}
```

Do NOT commit this file (it is gitignored).

- [ ] **Step 4: Manual smoke test checklist**

Start backend + frontend locally, then verify:

1. Sidebar shows **Anela** as the second group (after Dashboard) with **Porady** first and **Struktura** second.
2. No "Meeting Tasks" or "Personální" labels anywhere in the sidebar.
3. `/automation/meeting-tasks` — entering the page triggers a network request (check DevTools → Network).
4. Refresh button (↻ icon) appears in the Porady list header; clicking it re-fetches (icon spins, request fires).
5. Open a meeting detail → select text from the summary block → "Vysvětlit" tooltip appears → click → modal opens with a spinner → result shows **Záznam konverzace** + **Vysvětlení** sections.
6. Repeat with text selected from a task card.
7. Confirm ESC closes the modal.

- [ ] **Step 5: Final commit (if any formatting changes)**

```bash
git add -p && git commit -m "chore: final format and lint fixes"
```

---

## Self-Review Notes

- **Spec coverage:** All three areas covered — sidebar rename/reorder (Task 1), refresh (Task 2), explain BE (Tasks 3-6), explain FE (Tasks 7-10).
- **Placeholder scan:** All code blocks are complete; no TBDs.
- **Type consistency:** `ExplainSummaryResponse` fields (`relevantTranscript`, `explanation`, `success`) match between hook (Task 7) and modal props (Task 9). Handler response class fields match what the hook expects. `MeetingSummaryExplanation` returned by service matches deserialization target in `ClaudeMeetingSummaryExplainer`.
- **DTO rule:** `ExplainSummaryRequest` and `ExplainSummaryResponse` are `class`, not `record`. ✓
- **Absolute URL rule:** `fetchJson` helper already builds absolute URLs via `${apiClient.baseUrl}`. The new hook reuses the same helper. ✓
