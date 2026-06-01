# Recurring Jobs Next Run Column — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Next Run" column to the recurring jobs dashboard, computed server-side using NCrontab.Advanced and displayed in the user's browser timezone.

**Architecture:** `GetRecurringJobsListHandler` injects `TimeProvider` (already a registered singleton) and computes `NextRunAt` per-job after AutoMapper runs — enabled jobs get the next occurrence from `CrontabSchedule`, disabled jobs get `null`. The DTO field flows to the frontend via the auto-generated OpenAPI client.

**Tech Stack:** NCrontab.Advanced (already in Application layer), .NET 8 `TimeProvider`, AutoMapper, React + Tailwind CSS, Jest

---

## File Map

| Action | File |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJobsList/GetRecurringJobsListHandler.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs` |
| Modify | `frontend/src/pages/RecurringJobsPage.tsx` |
| Modify | `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts` |
| Auto-updated on build | `frontend/src/api/generated/api-client.ts` |

---

## Task 1: Add `NextRunAt` to DTO and write failing backend tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs`

- [ ] **Step 1: Add `NextRunAt` to `RecurringJobDto`**

Open `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs` and add one property:

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Contracts;

public class RecurringJobDto
{
    public string JobName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;
    public DateTime? NextRunAt { get; set; }
}
```

- [ ] **Step 2: Write failing tests for `NextRunAt` computation**

Add the following tests to the bottom of the existing `GetRecurringJobsListHandlerTests` class in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs`.

First, add `using Moq;` is already there. Add these two using statements at the top if not present:

```csharp
using NCrontab.Advanced;
```

Then add a `_timeProviderMock` field, update the constructor, and add the new tests. Replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class GetRecurringJobsListHandlerTests
{
    private readonly Mock<IRecurringJobConfigurationRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<GetRecurringJobsListHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetRecurringJobsListHandler _handler;

    // Fixed reference time used in all tests to avoid flakiness
    private static readonly DateTimeOffset FixedUtcNow = new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

    public GetRecurringJobsListHandlerTests()
    {
        _repositoryMock = new Mock<IRecurringJobConfigurationRepository>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<GetRecurringJobsListHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedUtcNow);
        _handler = new GetRecurringJobsListHandler(
            _repositoryMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Return_All_Jobs_From_Repository()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1"),
            new RecurringJobConfiguration("Job2", "Display 2", "Description 2", "0 1 * * *", false, "User2")
        };

        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto
            {
                JobName = "Job1",
                DisplayName = "Display 1",
                Description = "Description 1",
                CronExpression = "0 0 * * *",
                IsEnabled = true,
                LastModifiedBy = "User1"
            },
            new RecurringJobDto
            {
                JobName = "Job2",
                DisplayName = "Display 2",
                Description = "Description 2",
                CronExpression = "0 1 * * *",
                IsEnabled = false,
                LastModifiedBy = "User2"
            }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().HaveCount(2);
        result.Jobs[0].JobName.Should().Be("Job1");
        result.Jobs[1].JobName.Should().Be("Job2");
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_List_When_No_Jobs_Exist()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Call_Repository_GetAllAsync()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>();
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Map_Entities_To_Dtos()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>();

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mapperMock.Verify(
            m => m.Map<List<RecurringJobDto>>(jobs),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Log_Information_Messages()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Description 1", "0 0 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 0 * * *", IsEnabled = true }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Getting recurring jobs list")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retrieved") && v.ToString()!.Contains("recurring jobs")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenJobIsEnabled_SetsNextRunAtToFutureUtcDateTime()
    {
        // Arrange - fixed time is 2026-03-30 12:00:00 UTC; cron "0 13 * * *" next fires at 13:00 same day
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Desc", "0 13 * * *", true, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 13 * * *", IsEnabled = true }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — next occurrence of "0 13 * * *" after 12:00 UTC on 2026-03-30 is 13:00:00 UTC same day
        var expectedNextRun = new DateTime(2026, 3, 30, 13, 0, 0, DateTimeKind.Utc);
        result.Jobs[0].NextRunAt.Should().Be(expectedNextRun);
    }

    [Fact]
    public async Task Handle_WhenJobIsDisabled_SetsNextRunAtToNull()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job2", "Display 2", "Desc", "0 13 * * *", false, "User1")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job2", CronExpression = "0 13 * * *", IsEnabled = false }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Jobs[0].NextRunAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MixedEnabledDisabled_SetsNextRunAtCorrectly()
    {
        // Arrange
        var request = new GetRecurringJobsListRequest();
        var jobs = new List<RecurringJobConfiguration>
        {
            new RecurringJobConfiguration("Job1", "Display 1", "Desc", "0 13 * * *", true, "User1"),
            new RecurringJobConfiguration("Job2", "Display 2", "Desc", "0 3 * * *", false, "User2")
        };
        var jobDtos = new List<RecurringJobDto>
        {
            new RecurringJobDto { JobName = "Job1", CronExpression = "0 13 * * *", IsEnabled = true },
            new RecurringJobDto { JobName = "Job2", CronExpression = "0 3 * * *", IsEnabled = false }
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobs);

        _mapperMock
            .Setup(m => m.Map<List<RecurringJobDto>>(jobs))
            .Returns(jobDtos);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Jobs[0].NextRunAt.Should().NotBeNull();
        result.Jobs[0].NextRunAt.Should().BeAfter(FixedUtcNow.UtcDateTime);
        result.Jobs[1].NextRunAt.Should().BeNull();
    }
}
```

- [ ] **Step 3: Run tests — they must FAIL because handler doesn't accept `TimeProvider` yet**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRecurringJobsListHandlerTests" --no-build 2>&1 | tail -20
```

Expected: compilation error — `GetRecurringJobsListHandler` constructor doesn't take `TimeProvider`.

---

## Task 2: Update handler to inject `TimeProvider` and compute `NextRunAt`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJobsList/GetRecurringJobsListHandler.cs`

- [ ] **Step 1: Replace handler implementation**

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using NCrontab.Advanced;

namespace Anela.Heblo.Application.Features.BackgroundJobs.UseCases.GetRecurringJobsList;

public class GetRecurringJobsListHandler : IRequestHandler<GetRecurringJobsListRequest, GetRecurringJobsListResponse>
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetRecurringJobsListHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetRecurringJobsListHandler(
        IRecurringJobConfigurationRepository repository,
        IMapper mapper,
        ILogger<GetRecurringJobsListHandler> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<GetRecurringJobsListResponse> Handle(
        GetRecurringJobsListRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting recurring jobs list");

        var jobs = await _repository.GetAllAsync(cancellationToken);
        var jobDtos = _mapper.Map<List<RecurringJobDto>>(jobs);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        for (var i = 0; i < jobs.Count; i++)
        {
            jobDtos[i].NextRunAt = jobs[i].IsEnabled
                ? CrontabSchedule.Parse(jobs[i].CronExpression).GetNextOccurrence(utcNow)
                : null;
        }

        _logger.LogInformation("Retrieved {Count} recurring jobs", jobDtos.Count);

        return new GetRecurringJobsListResponse
        {
            Jobs = jobDtos
        };
    }
}
```

- [ ] **Step 2: Run backend tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRecurringJobsListHandlerTests" 2>&1 | tail -30
```

Expected: all tests PASS (7 tests total — 4 existing + 3 new).

- [ ] **Step 3: Run `dotnet format` and full backend build**

```bash
cd backend
dotnet format src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet build Anela.Heblo.sln 2>&1 | tail -20
```

Expected: no warnings about formatting, build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RecurringJobDto.cs
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/GetRecurringJobsList/GetRecurringJobsListHandler.cs
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/GetRecurringJobsListHandlerTests.cs
git commit -m "feat: add NextRunAt to RecurringJobDto, compute via NCrontab in handler"
```

---

## Task 3: Regenerate API client

The OpenAPI TypeScript client is generated from the backend's Swagger spec on `dotnet build`. After the backend build above, regenerate the frontend client so `RecurringJobDto` gains `nextRunAt?: string | undefined`.

- [ ] **Step 1: Check if client needs regeneration**

```bash
grep -n "nextRunAt" frontend/src/api/generated/api-client.ts | head -5
```

If the field is already present, skip to Task 4. If not:

- [ ] **Step 2: Regenerate the API client**

```bash
cd backend
dotnet build Anela.Heblo.sln
```

The build process auto-generates `frontend/src/api/generated/api-client.ts`. Verify the new field:

```bash
grep -n "nextRunAt" frontend/src/api/generated/api-client.ts | head -5
```

Expected output includes something like:
```
nextRunAt?: string | undefined;
```

- [ ] **Step 3: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate API client with nextRunAt field"
```

---

## Task 4: Add "Next Run" column to `RecurringJobsPage.tsx`

**Files:**
- Modify: `frontend/src/pages/RecurringJobsPage.tsx`

- [ ] **Step 1: Add the column header**

In `RecurringJobsPage.tsx`, find the `<thead>` block. After the `Last Modified` `<th>` and before the `Status` `<th>`, insert:

```tsx
<th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
  Next Run
</th>
```

The thead should now read (showing the relevant section):
```tsx
<th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
  Last Modified
</th>
<th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
  Next Run
</th>
<th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
  Status
</th>
```

- [ ] **Step 2: Add the column cell**

In the `<tbody>` section, find the `<td>` for `lastModifiedAt` (the one containing `formatDate(job.lastModifiedAt)`). After its closing `</td>` and before the Status `<td>`, insert:

```tsx
<td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
  {job.nextRunAt
    ? formatDate(job.nextRunAt)
    : '—'}
</td>
```

Note: `formatDate` is already defined in the component and formats using `cs-CZ` locale with date and time — reuse it rather than calling `toLocaleString()` directly, to keep formatting consistent with the "Last Modified" column.

- [ ] **Step 3: Run frontend lint**

```bash
cd frontend
npm run lint 2>&1 | tail -20
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/RecurringJobsPage.tsx
git commit -m "feat: add Next Run column to recurring jobs dashboard"
```

---

## Task 5: Add frontend test for `nextRunAt`

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts`

- [ ] **Step 1: Add test for `useRecurringJobsQuery` returning `nextRunAt`**

Add a new `describe` block at the bottom of `frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts`:

```typescript
describe('useRecurringJobsQuery', () => {
  const mockApiClient = {
    recurringJobs_GetRecurringJobs: jest.fn(),
  };

  beforeEach(() => {
    jest.clearAllMocks();
    mockGetAuthenticatedApiClient.mockReturnValue(mockApiClient as any);
  });

  it('returns nextRunAt for enabled jobs and null for disabled jobs', async () => {
    const enabledNextRun = '2026-03-30T13:00:00Z';
    mockApiClient.recurringJobs_GetRecurringJobs.mockResolvedValue({
      success: true,
      jobs: [
        {
          jobName: 'job-enabled',
          displayName: 'Enabled Job',
          cronExpression: '0 13 * * *',
          isEnabled: true,
          nextRunAt: enabledNextRun,
          lastModifiedAt: '2026-01-01T00:00:00Z',
          lastModifiedBy: 'user',
        },
        {
          jobName: 'job-disabled',
          displayName: 'Disabled Job',
          cronExpression: '0 3 * * *',
          isEnabled: false,
          nextRunAt: null,
          lastModifiedAt: '2026-01-01T00:00:00Z',
          lastModifiedBy: 'user',
        },
      ],
    });

    const { result } = renderHook(() => useRecurringJobsQuery(), {
      wrapper: createWrapper,
    });

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(result.current.data).toHaveLength(2);
    expect(result.current.data![0].nextRunAt).toBe(enabledNextRun);
    expect(result.current.data![1].nextRunAt).toBeNull();
  });
});
```

Also add the import for `useRecurringJobsQuery` at the top of the file:

```typescript
import { useUpdateRecurringJobCronMutation, useRecurringJobsQuery } from '../useRecurringJobs';
```

- [ ] **Step 2: Run frontend tests**

```bash
cd frontend
npm test -- --testPathPattern="useRecurringJobs" --watchAll=false 2>&1 | tail -30
```

Expected: all tests PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/__tests__/useRecurringJobs.test.ts
git commit -m "test: verify nextRunAt is passed through useRecurringJobsQuery"
```

---

## Task 6: Final verification

- [ ] **Step 1: Run all backend tests**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -15
```

Expected: all tests pass.

- [ ] **Step 2: Run all frontend tests**

```bash
cd frontend
npm test -- --watchAll=false 2>&1 | tail -15
```

Expected: all tests pass.

- [ ] **Step 3: Run frontend build**

```bash
cd frontend
npm run build 2>&1 | tail -15
```

Expected: build succeeds with no errors.
