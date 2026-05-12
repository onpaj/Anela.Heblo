# Subtask 3: Read Handlers — List & Detail

**Parent Epic:** Meeting Task Validation Checkpoint

CRITICAL - This is part of epic, you **MUST** use epic branch - feature/meeting_tasks

## Task 5: GetTranscriptList & GetTranscriptDetail Handlers

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`

- [ ] **Step 1: Create DTOs**

> **Note:** `SourceEmail` is replaced by `PlaudRecordingId`. `PlaudCreatedAt` added for display.

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class MeetingTranscriptDto
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }
    public int TaskCount { get; set; }
    public int ApprovedTaskCount { get; set; }
    public int RejectedTaskCount { get; set; }
    public List<ProposedTaskDto> Tasks { get; set; } = new();
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs
namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class ProposedTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = null!;
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}
```

- [ ] **Step 2: Create GetTranscriptList request/response/handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListRequest : IRequest<GetTranscriptListResponse>
{
    public string? StatusFilter { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListResponse.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListResponse : BaseResponse
{
    public List<MeetingTranscriptDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListHandler : IRequestHandler<GetTranscriptListRequest, GetTranscriptListResponse>
{
    private readonly IMeetingTranscriptRepository _repository;

    public GetTranscriptListHandler(IMeetingTranscriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTranscriptListResponse> Handle(GetTranscriptListRequest request, CancellationToken cancellationToken)
    {
        MeetingTranscriptStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(request.StatusFilter) &&
            Enum.TryParse<MeetingTranscriptStatus>(request.StatusFilter, true, out var parsed))
        {
            statusFilter = parsed;
        }

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter, request.PageNumber, request.PageSize, cancellationToken);

        return new GetTranscriptListResponse
        {
            Items = items.Select(t => new MeetingTranscriptDto
            {
                Id = t.Id,
                PlaudRecordingId = t.PlaudRecordingId,
                PlaudCreatedAt = t.PlaudCreatedAt,
                Subject = t.Subject,
                Summary = t.Summary,
                Status = t.Status.ToString(),
                ReceivedAt = t.ReceivedAt,
                ReviewedAt = t.ReviewedAt,
                ReviewedByUser = t.ReviewedByUser,
                TaskCount = t.Tasks.Count,
                ApprovedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
                RejectedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
                Tasks = new()
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
```

- [ ] **Step 3: Create GetTranscriptDetail request/response/handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailRequest : IRequest<GetTranscriptDetailResponse>
{
    public Guid Id { get; set; }
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailResponse.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailResponse : BaseResponse
{
    public MeetingTranscriptDto Transcript { get; set; } = null!;
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs
using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailHandler : IRequestHandler<GetTranscriptDetailRequest, GetTranscriptDetailResponse>
{
    private readonly IMeetingTranscriptRepository _repository;

    public GetTranscriptDetailHandler(IMeetingTranscriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTranscriptDetailResponse> Handle(GetTranscriptDetailRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transcript == null)
        {
            return new GetTranscriptDetailResponse
            {
                Success = false,
                ErrorCode = Application.Shared.ErrorCodes.NotFound
            };
        }

        return new GetTranscriptDetailResponse
        {
            Transcript = new MeetingTranscriptDto
            {
                Id = transcript.Id,
                PlaudRecordingId = transcript.PlaudRecordingId,
                PlaudCreatedAt = transcript.PlaudCreatedAt,
                Subject = transcript.Subject,
                Summary = transcript.Summary,
                Status = transcript.Status.ToString(),
                ReceivedAt = transcript.ReceivedAt,
                ReviewedAt = transcript.ReviewedAt,
                ReviewedByUser = transcript.ReviewedByUser,
                TaskCount = transcript.Tasks.Count,
                ApprovedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
                RejectedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
                Tasks = transcript.Tasks.Select(t => new ProposedTaskDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    Assignee = t.Assignee,
                    DueDate = t.DueDate,
                    Status = t.Status.ToString(),
                    ExternalTaskId = t.ExternalTaskId,
                    IsManuallyAdded = t.IsManuallyAdded
                }).ToList()
            }
        };
    }
}
```

- [ ] **Step 4: Write tests for both handlers**

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptListHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();

    [Fact]
    public async Task Handle_ReturnsPagedResults()
    {
        var transcripts = new List<MeetingTranscript>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PlaudRecordingId = "rec-001",
                PlaudCreatedAt = DateTime.UtcNow,
                Subject = "Sprint Planning",
                Summary = "Summary 1",
                RawTranscript = "Transcript 1",
                Status = MeetingTranscriptStatus.PendingReview,
                ReceivedAt = DateTime.UtcNow,
                Tasks = new List<ProposedTask>
                {
                    new() { Id = Guid.NewGuid(), Title = "Task 1", Status = ProposedTaskStatus.Pending, Assignee = "Alice", Description = "" }
                }
            }
        };
        _repoMock.Setup(r => r.GetListAsync(null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((transcripts, 1));

        var handler = new GetTranscriptListHandler(_repoMock.Object);
        var result = await handler.Handle(new GetTranscriptListRequest(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal("PendingReview", result.Items[0].Status);
        Assert.Equal("rec-001", result.Items[0].PlaudRecordingId);
        Assert.Equal(1, result.Items[0].TaskCount);
        Assert.Equal(1, result.TotalCount);
    }
}
```

```csharp
// backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class GetTranscriptDetailHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _repoMock = new();

    [Fact]
    public async Task Handle_ExistingTranscript_ReturnsDetailWithTasks()
    {
        var id = Guid.NewGuid();
        var transcript = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec-001",
            PlaudCreatedAt = DateTime.UtcNow,
            Subject = "Sprint Planning",
            Summary = "Summary",
            RawTranscript = "Full transcript...",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), Title = "Task 1", Description = "Desc", Assignee = "Alice", Status = ProposedTaskStatus.Pending }
            }
        };
        _repoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(transcript);

        var handler = new GetTranscriptDetailHandler(_repoMock.Object);
        var result = await handler.Handle(new GetTranscriptDetailRequest { Id = id }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Sprint Planning", result.Transcript.Subject);
        Assert.Equal("rec-001", result.Transcript.PlaudRecordingId);
        Assert.Single(result.Transcript.Tasks);
    }

    [Fact]
    public async Task Handle_NonExistentTranscript_ReturnsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        var handler = new GetTranscriptDetailHandler(_repoMock.Object);
        var result = await handler.Handle(new GetTranscriptDetailRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.False(result.Success);
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetTranscriptListHandlerTests|FullyQualifiedName~GetTranscriptDetailHandlerTests"`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs
git commit -m "feat(meeting-tasks): add GetTranscriptList and GetTranscriptDetail handlers with tests"
```

---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).