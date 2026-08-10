# Delete Meeting Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a meeting manager permanently delete one meeting note — transcript, summary, proposed tasks and access grants — from the meeting note detail page, without it being re-imported from Plaud afterwards.

**Architecture:** A hard delete of the `MeetingTranscript` row (its `ProposedTasks` and `MeetingAccessGrants` already cascade in EF and in PostgreSQL), plus a new `DeletedPlaudRecordings` tombstone table keyed by `PlaudRecordingId` that `IngestPlaudRecordingHandler` checks so `PlaudPollingJob` never re-ingests the recording. A new MediatR use case behind `DELETE /api/meeting-tasks/{transcriptId}`, gated by `anela.meetings.write`, drives it. The frontend adds a manager-only "Smazat" button plus a confirmation dialog on the detail page.

**Tech Stack:** .NET 8, MediatR, EF Core 8 + Npgsql, xUnit + Moq + FluentAssertions; React 18 + TypeScript, TanStack Query, Tailwind, Jest + React Testing Library.

**Design spec:** `docs/superpowers/specs/2026-08-10-delete-meeting-notes-design.md`

## Global Constraints

- DTOs and API request/response types are **classes, never C# records** (OpenAPI generator mishandles record parameter order).
- Every Application-layer `*Response` class **must inherit `BaseResponse`** — a reflection contract test fails in CI otherwise.
- No new `ErrorCodes` entries. Reuse `ErrorCodes.Forbidden` (403) and `ErrorCodes.ResourceNotFound` (404). Adding an `ErrorCodes` member would also require an `ErrorHandlingTests` module-range update and a Czech `i18n.ts` translation.
- MeetingTasks handlers are auto-registered by the MediatR assembly scan in `ApplicationModule`. Do **not** add per-handler DI registrations. MeetingTasks validates inline in handlers; do **not** add FluentValidation validators.
- EF entity configurations are discovered by `modelBuilder.ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:184`). A new `IEntityTypeConfiguration<T>` in `Anela.Heblo.Persistence` needs no manual registration, but the `DbSet<T>` must be added by hand.
- All `DateTime` columns use `.AsUtcTimestamp()` (PostgreSQL `timestamp without time zone`) and store `DateTime.UtcNow`.
- Database migrations are **created but not applied automatically**. Generate the migration; do not run `dotnet ef database update` against a shared database.
- The EF InMemory provider (used by repository tests) does **not** support `ExecuteDelete`/`ExecuteUpdate`. Use `Remove` / `RemoveRange`.
- Frontend tests run with `react-scripts test`, never `npx jest` (jest alone hits TS parse errors).
- Frontend build gate is `CI=false npm run build`, not `npx tsc --noEmit` (tsc false-greens on react-i18next `.d.ts` parse errors).
- UI copy is Czech.
- `dotnet test` hangs when another worktree runs it concurrently — always `dotnet build` first, then `dotnet test --no-build -p:UseSharedCompilation=false`.

---

## File Structure

**Backend — create**
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/DeletedPlaudRecording.cs` — tombstone entity.
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/DeletedPlaudRecordingConfiguration.cs` — table/index mapping.
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddDeletedPlaudRecordings.cs` — generated.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/DeleteMeetingTranscriptHandlerTests.cs`

**Backend — modify**
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` — add `DeleteAsync`, `IsPlaudRecordingDeletedAsync`.
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` — implement both.
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:131` — add `DbSet<DeletedPlaudRecording>`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs:34-39` — tombstone guard.
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` — `DELETE {transcriptId:guid}` action.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs` — repository tests.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs` — guard test.

**Frontend — create**
- `frontend/src/components/pages/automation/ConfirmDeleteMeetingDialog.tsx` — confirmation dialog.
- `frontend/src/components/pages/automation/__tests__/ConfirmDeleteMeetingDialog.test.tsx`
- `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.delete.test.tsx`

**Frontend — modify**
- `frontend/src/api/hooks/useMeetingTasks.ts` — `useDeleteMeeting` hook.
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` — button, dialog wiring, navigate-away.

---

## Task 1: Tombstone entity, repository delete, migration

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/DeletedPlaudRecording.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/DeletedPlaudRecordingConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (line ~131, Meeting Tasks module DbSet block)
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `Anela.Heblo.Domain.Features.MeetingTasks.DeletedPlaudRecording` with properties `Guid Id`, `string PlaudRecordingId`, `DateTime DeletedAt`, `string DeletedByUserEmail`.
  - `IMeetingTranscriptRepository.DeleteAsync(MeetingTranscript transcript, string deletedByUserEmail, CancellationToken ct = default) : Task` — removes the transcript, writes the tombstone, saves once.
  - `IMeetingTranscriptRepository.IsPlaudRecordingDeletedAsync(string plaudRecordingId, CancellationToken ct = default) : Task<bool>`.
  - `ApplicationDbContext.DeletedPlaudRecordings : DbSet<DeletedPlaudRecording>`.

- [ ] **Step 1: Write the failing repository tests**

Append these three tests to `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs`, immediately before the `private static MeetingTranscript BuildTranscript(...)` helper (around line 125). They use the existing `BuildTranscript` helper and the existing `_context` / `_repository` fields.

```csharp
    [Fact]
    public async Task DeleteAsync_RemovesTranscriptWithTasksAndAccessGrants()
    {
        // Arrange
        var transcript = BuildTranscript("plaud-delete-1", taskCount: 2);
        transcript.AccessLevel = MeetingAccessLevel.Restricted;
        transcript.AccessGrants.Add(new MeetingAccessGrant
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = transcript.Id,
            UserEmail = "alice@anela.cz",
            UserDisplayName = "Alice",
            GrantedAt = DateTime.UtcNow,
            GrantedByUserEmail = "ondra@anela.cz"
        });
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Load through the repository so tasks and grants are tracked (cascade needs them loaded)
        var loaded = await _repository.GetByIdAsync(transcript.Id);

        // Act
        await _repository.DeleteAsync(loaded!, "ondra@anela.cz");

        // Assert
        (await _context.MeetingTranscripts.CountAsync()).Should().Be(0);
        (await _context.ProposedTasks.CountAsync()).Should().Be(0);
        (await _context.MeetingAccessGrants.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WritesTombstoneWithRecordingIdAndUser()
    {
        // Arrange
        var transcript = BuildTranscript("plaud-delete-2", taskCount: 0);
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();
        var loaded = await _repository.GetByIdAsync(transcript.Id);

        // Act
        await _repository.DeleteAsync(loaded!, "ondra@anela.cz");

        // Assert
        var tombstone = await _context.DeletedPlaudRecordings.SingleAsync();
        tombstone.PlaudRecordingId.Should().Be("plaud-delete-2");
        tombstone.DeletedByUserEmail.Should().Be("ondra@anela.cz");
        tombstone.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task IsPlaudRecordingDeletedAsync_ReturnsTrueOnlyForTombstonedRecordings()
    {
        // Arrange
        var transcript = BuildTranscript("plaud-delete-3", taskCount: 0);
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();
        var loaded = await _repository.GetByIdAsync(transcript.Id);
        await _repository.DeleteAsync(loaded!, "ondra@anela.cz");

        // Act & Assert
        (await _repository.IsPlaudRecordingDeletedAsync("plaud-delete-3")).Should().BeTrue();
        (await _repository.IsPlaudRecordingDeletedAsync("plaud-never-deleted")).Should().BeFalse();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests"
```

Expected: the build fails with `'IMeetingTranscriptRepository' does not contain a definition for 'DeleteAsync'` and `'ApplicationDbContext' does not contain a definition for 'DeletedPlaudRecordings'`. A compile failure is the expected RED here.

- [ ] **Step 3: Create the tombstone entity**

Create `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/DeletedPlaudRecording.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

/// <summary>
/// Marks a Plaud recording whose meeting transcript was deleted by a user.
/// Prevents <c>PlaudPollingJob</c> from re-ingesting the recording while it is
/// still inside the polling window. Deliberately stores no meeting content —
/// only who deleted which recording and when.
/// </summary>
public class DeletedPlaudRecording
{
    public Guid Id { get; set; }

    public string PlaudRecordingId { get; set; } = null!;

    public DateTime DeletedAt { get; set; }

    public string DeletedByUserEmail { get; set; } = null!;
}
```

- [ ] **Step 4: Create the EF configuration**

Create `backend/src/Anela.Heblo.Persistence/MeetingTasks/DeletedPlaudRecordingConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class DeletedPlaudRecordingConfiguration : IEntityTypeConfiguration<DeletedPlaudRecording>
{
    public void Configure(EntityTypeBuilder<DeletedPlaudRecording> builder)
    {
        builder.ToTable("DeletedPlaudRecordings", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlaudRecordingId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DeletedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.DeletedByUserEmail)
            .IsRequired()
            .HasMaxLength(320);

        builder.HasIndex(x => x.PlaudRecordingId)
            .IsUnique()
            .HasDatabaseName("UX_DeletedPlaudRecordings_PlaudRecordingId");
    }
}
```

- [ ] **Step 5: Register the DbSet**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, in the `// Meeting Tasks module` block, add the fourth set after `MeetingAccessGrants`:

```csharp
    // Meeting Tasks module
    public DbSet<MeetingTranscript> MeetingTranscripts { get; set; } = null!;
    public DbSet<ProposedTask> ProposedTasks { get; set; } = null!;
    public DbSet<MeetingAccessGrant> MeetingAccessGrants { get; set; } = null!;
    public DbSet<DeletedPlaudRecording> DeletedPlaudRecordings { get; set; } = null!;
```

- [ ] **Step 6: Extend the repository interface**

In `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`, add these members after `ReplacePendingTasksAsync` and before `SaveChangesAsync`:

```csharp
    /// <summary>
    /// Permanently removes the transcript together with its proposed tasks and access
    /// grants (cascade), and records a <see cref="DeletedPlaudRecording"/> tombstone so
    /// the Plaud polling job does not re-ingest the recording. Saves in one transaction.
    /// </summary>
    Task DeleteAsync(MeetingTranscript transcript, string deletedByUserEmail, CancellationToken ct = default);

    Task<bool> IsPlaudRecordingDeletedAsync(string plaudRecordingId, CancellationToken ct = default);
```

- [ ] **Step 7: Implement the repository methods**

In `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`, add after `ReplacePendingTasksAsync` and before `SaveChangesAsync`:

```csharp
    public async Task DeleteAsync(MeetingTranscript transcript, string deletedByUserEmail, CancellationToken ct = default)
    {
        _context.MeetingTranscripts.Remove(transcript);

        await _context.DeletedPlaudRecordings.AddAsync(new DeletedPlaudRecording
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = transcript.PlaudRecordingId,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserEmail = deletedByUserEmail
        }, ct);

        await _context.SaveChangesAsync(ct);
    }

    public Task<bool> IsPlaudRecordingDeletedAsync(string plaudRecordingId, CancellationToken ct = default)
    {
        return _context.DeletedPlaudRecordings
            .AnyAsync(x => x.PlaudRecordingId == plaudRecordingId, ct);
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests"
```

Expected: PASS, including the three new tests.

- [ ] **Step 9: Generate the migration**

```bash
dotnet ef migrations add AddDeletedPlaudRecordings \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Open the generated `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddDeletedPlaudRecordings.cs` and confirm it creates only the `DeletedPlaudRecordings` table plus the unique index `UX_DeletedPlaudRecordings_PlaudRecordingId`, and touches nothing else. If it contains unrelated changes, remove the migration (`dotnet ef migrations remove --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`) and investigate before continuing.

**Do not run `dotnet ef database update`** — migrations are applied manually in this project.

- [ ] **Step 10: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Domain backend/src/Anela.Heblo.Persistence backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/DeletedPlaudRecording.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/DeletedPlaudRecordingConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Migrations \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs
git commit -m "feat: add meeting transcript delete with plaud tombstone persistence"
```

---

## Task 2: Delete use case and API endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/DeleteMeetingTranscriptHandlerTests.cs`

**Interfaces:**
- Consumes: `IMeetingTranscriptRepository.DeleteAsync(MeetingTranscript, string, CancellationToken)` from Task 1.
- Produces:
  - `DeleteMeetingTranscriptRequest : IRequest<DeleteMeetingTranscriptResponse>` with `Guid TranscriptId { get; set; }`.
  - `DeleteMeetingTranscriptResponse : BaseResponse` — no extra members.
  - `DELETE /api/meeting-tasks/{transcriptId:guid}` returning `DeleteMeetingTranscriptResponse`.

- [ ] **Step 1: Write the failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/DeleteMeetingTranscriptHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class DeleteMeetingTranscriptHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<ICurrentUserService> _mockCurrentUser;
    private readonly Mock<ILogger<DeleteMeetingTranscriptHandler>> _mockLogger;
    private readonly DeleteMeetingTranscriptHandler _handler;

    public DeleteMeetingTranscriptHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockCurrentUser = new Mock<ICurrentUserService>();
        _mockLogger = new Mock<ILogger<DeleteMeetingTranscriptHandler>>();

        _mockAccessGuard.Setup(g => g.IsManager()).Returns(true);
        _mockCurrentUser
            .Setup(c => c.GetCurrentUser())
            .Returns(new CurrentUser("id-1", "Ondra", "ondra@anela.cz", true));

        _handler = new DeleteMeetingTranscriptHandler(
            _mockRepository.Object,
            _mockAccessGuard.Object,
            _mockCurrentUser.Object,
            _mockLogger.Object);
    }

    private MeetingTranscript SetupTranscript(out Guid id)
    {
        id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_1",
            Subject = "Subject",
            Summary = "Summary",
            RawTranscript = "Transcript",
            Status = MeetingTranscriptStatus.PendingReview,
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        return entity;
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotManager_ReturnsForbiddenAndDoesNotDelete()
    {
        // Arrange
        _mockAccessGuard.Setup(g => g.IsManager()).Returns(false);
        SetupTranscript(out var id);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = id },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _mockRepository.Verify(
            r => r.DeleteAsync(It.IsAny<MeetingTranscript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTranscriptDoesNotExist_ReturnsResourceNotFound()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _mockRepository
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = missingId },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockRepository.Verify(
            r => r.DeleteAsync(It.IsAny<MeetingTranscript>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenManagerDeletesExistingTranscript_DeletesWithCurrentUserEmail()
    {
        // Arrange
        var entity = SetupTranscript(out var id);

        // Act
        var response = await _handler.Handle(
            new DeleteMeetingTranscriptRequest { TranscriptId = id },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _mockRepository.Verify(
            r => r.DeleteAsync(entity, "ondra@anela.cz", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build Anela.Heblo.sln
```

Expected: build fails — `The type or namespace name 'DeleteMeetingTranscript' does not exist`.

- [ ] **Step 3: Create the request**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptRequest : IRequest<DeleteMeetingTranscriptResponse>
{
    public Guid TranscriptId { get; set; }
}
```

- [ ] **Step 4: Create the response**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptResponse : BaseResponse
{
    public DeleteMeetingTranscriptResponse() { }

    public DeleteMeetingTranscriptResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 5: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/DeleteMeetingTranscriptHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptHandler : IRequestHandler<DeleteMeetingTranscriptRequest, DeleteMeetingTranscriptResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteMeetingTranscriptHandler> _logger;

    public DeleteMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        ILogger<DeleteMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<DeleteMeetingTranscriptResponse> Handle(
        DeleteMeetingTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        if (!_accessGuard.IsManager())
        {
            _logger.LogWarning("Non-manager attempted to delete meeting transcript {TranscriptId}", request.TranscriptId);
            return new DeleteMeetingTranscriptResponse(ErrorCodes.Forbidden);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new DeleteMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        var userEmail = _currentUserService.GetCurrentUser().Email ?? string.Empty;
        var plaudRecordingId = transcript.PlaudRecordingId;

        await _repository.DeleteAsync(transcript, userEmail, cancellationToken);

        _logger.LogWarning(
            "Meeting transcript {TranscriptId} (plaud {PlaudRecordingId}) deleted by {User}",
            request.TranscriptId, plaudRecordingId, userEmail);

        return new DeleteMeetingTranscriptResponse();
    }
}
```

- [ ] **Step 6: Run the handler tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~DeleteMeetingTranscriptHandlerTests"
```

Expected: PASS (3 tests).

- [ ] **Step 7: Add the controller endpoint**

In `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`, add the using alongside the other MeetingTasks use-case usings (they are alphabetically ordered — it goes after `...UseCases.AddProposedTask;`):

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;
```

Then add this action at the end of the class, after the `Reimport` action:

```csharp
    [HttpDelete("{transcriptId:guid}")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<DeleteMeetingTranscriptResponse>> Delete(
        Guid transcriptId,
        CancellationToken ct = default)
        => HandleResponse(await _mediator.Send(new DeleteMeetingTranscriptRequest { TranscriptId = transcriptId }, ct));
```

- [ ] **Step 8: Verify the build and the whole MeetingTasks suite**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~MeetingTasks"
```

Expected: build succeeds, all MeetingTasks tests PASS.

- [ ] **Step 9: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/src/Anela.Heblo.API backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript \
        backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/DeleteMeetingTranscriptHandlerTests.cs
git commit -m "feat: add DELETE /api/meeting-tasks/{id} endpoint for meeting managers"
```

---

## Task 3: Stop the Plaud poller re-importing deleted recordings

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs:34-39`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

**Interfaces:**
- Consumes: `IMeetingTranscriptRepository.IsPlaudRecordingDeletedAsync(string, CancellationToken)` from Task 1.
- Produces: nothing new. `IngestPlaudRecordingResponse` is unchanged — a tombstoned recording returns the existing `Skipped = true`.

- [ ] **Step 1: Write the failing test**

Append to `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task Handle_WhenRecordingWasDeletedByUser_SkipsWithoutCallingPlaud()
    {
        // Arrange
        const string recordingId = "rec_deleted";
        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = "Private meeting",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository
            .Setup(r => r.IsPlaudRecordingDeletedAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Skipped.Should().BeTrue();
        _mockPlaudClient.Verify(
            c => c.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPlaudClient.Verify(
            c => c.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(
            r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests.Handle_WhenRecordingWasDeletedByUser_SkipsWithoutCallingPlaud"
```

Expected: FAIL. The unstubbed `GetFileDetailAsync` returns `null` on the loose mock, so the handler throws a `NullReferenceException` before reaching any assertion.

- [ ] **Step 3: Add the tombstone guard**

In `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`, insert this block immediately after the existing `ExistsByPlaudIdAsync` early return and before the `// Check if Plaud has finished generating...` comment:

```csharp
        // Recording was deliberately deleted by a user — never bring it back
        if (await _repository.IsPlaudRecordingDeletedAsync(request.PlaudRecordingId, cancellationToken))
        {
            _logger.LogInformation(
                "Recording {RecordingId} was deleted by a user, not re-ingesting", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true };
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"
```

Expected: PASS, including the pre-existing ingest tests (they stub `ExistsByPlaudIdAsync` only; the unstubbed `IsPlaudRecordingDeletedAsync` returns `false` by default on a loose Moq mock, which is the non-tombstoned path).

- [ ] **Step 5: Format and commit**

```bash
dotnet format Anela.Heblo.sln --include backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
git commit -m "feat: skip plaud ingest for recordings deleted by a user"
```

---

## Task 4: Frontend delete hook and confirmation dialog

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts` (append after the `useReimportMeeting` section at the end of the file)
- Create: `frontend/src/components/pages/automation/ConfirmDeleteMeetingDialog.tsx`
- Test: `frontend/src/components/pages/automation/__tests__/ConfirmDeleteMeetingDialog.test.tsx`

**Interfaces:**
- Consumes: `DELETE /api/meeting-tasks/{transcriptId}` from Task 2; the file-local `fetchJson<T>(path, init)` helper and `MEETING_TASKS_KEYS` already in `useMeetingTasks.ts`.
- Produces:
  - `useDeleteMeeting(): UseMutationResult<DeleteMeetingResponse, Error, string>` — the mutation variable is the transcript id.
  - `interface DeleteMeetingResponse { success: boolean; errorCode?: string }`.
  - Default-exported `ConfirmDeleteMeetingDialog` component with props
    `{ isOpen: boolean; subject: string; isDeleting: boolean; error: string | null; onConfirm: () => void; onCancel: () => void }`.

- [ ] **Step 1: Write the failing dialog test**

Create `frontend/src/components/pages/automation/__tests__/ConfirmDeleteMeetingDialog.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ConfirmDeleteMeetingDialog from '../ConfirmDeleteMeetingDialog';

const baseProps = {
  isOpen: true,
  subject: 'Schůzka s týmem',
  isDeleting: false,
  error: null,
  onConfirm: jest.fn(),
  onCancel: jest.fn(),
};

beforeEach(() => jest.clearAllMocks());

it('renders nothing when closed', () => {
  const { container } = render(<ConfirmDeleteMeetingDialog {...baseProps} isOpen={false} />);
  expect(container).toBeEmptyDOMElement();
});

it('names the meeting and explains what is deleted', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  expect(screen.getByText(/Schůzka s týmem/)).toBeInTheDocument();
  expect(screen.getByText(/přepis/i)).toBeInTheDocument();
  expect(screen.getByText(/Planneru/i)).toBeInTheDocument();
});

it('calls onConfirm when the delete button is clicked', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
  expect(baseProps.onConfirm).toHaveBeenCalledTimes(1);
});

it('calls onCancel when the cancel button is clicked', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} />);
  fireEvent.click(screen.getByRole('button', { name: /zrušit/i }));
  expect(baseProps.onCancel).toHaveBeenCalledTimes(1);
});

it('disables both buttons and shows progress while deleting', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} isDeleting />);
  expect(screen.getByRole('button', { name: /mažu/i })).toBeDisabled();
  expect(screen.getByRole('button', { name: /zrušit/i })).toBeDisabled();
});

it('shows the error message when deletion failed', () => {
  render(<ConfirmDeleteMeetingDialog {...baseProps} error="Smazání se nezdařilo." />);
  expect(screen.getByText('Smazání se nezdařilo.')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern=ConfirmDeleteMeetingDialog
```

Expected: FAIL with `Cannot find module '../ConfirmDeleteMeetingDialog'`.

- [ ] **Step 3: Create the dialog component**

Create `frontend/src/components/pages/automation/ConfirmDeleteMeetingDialog.tsx`:

```tsx
import React from 'react';
import { AlertTriangle, X } from 'lucide-react';

interface ConfirmDeleteMeetingDialogProps {
  isOpen: boolean;
  subject: string;
  isDeleting: boolean;
  error: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

const ConfirmDeleteMeetingDialog: React.FC<ConfirmDeleteMeetingDialogProps> = ({
  isOpen,
  subject,
  isDeleting,
  error,
  onConfirm,
  onCancel,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
        onClick={isDeleting ? undefined : onCancel}
      />

      {/* Dialog */}
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          <button
            onClick={onCancel}
            disabled={isDeleting}
            className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 disabled:opacity-50"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5" />
          </button>

          <div className="flex items-center justify-center w-12 h-12 mx-auto bg-red-100 dark:bg-red-900/30 rounded-full mb-4">
            <AlertTriangle className="h-6 w-6 text-red-600 dark:text-red-400" />
          </div>

          <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text text-center mb-2">
            Smazat schůzku?
          </h3>

          <p className="text-sm text-gray-600 dark:text-graphite-muted text-center mb-3">
            {`Schůzka „${subject}" bude trvale smazána včetně souhrnu, přepisu, navržených úkolů a přístupových oprávnění. Tuto akci nelze vrátit zpět.`}
          </p>

          <p className="text-sm text-gray-500 dark:text-graphite-faint text-center mb-6">
            Schůzka se už znovu nenačte z Plaudu. Úkoly, které už byly odeslány do Planneru, tam zůstanou.
          </p>

          {error && (
            <p className="text-sm text-red-600 dark:text-red-400 text-center mb-4">{error}</p>
          )}

          <div className="flex gap-3">
            <button
              onClick={onCancel}
              disabled={isDeleting}
              className="flex-1 px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface border border-gray-300 dark:border-graphite-border rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Zrušit
            </button>
            <button
              onClick={onConfirm}
              disabled={isDeleting}
              className="flex-1 px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isDeleting ? 'Mažu...' : 'Smazat'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ConfirmDeleteMeetingDialog;
```

- [ ] **Step 4: Run the dialog test to verify it passes**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern=ConfirmDeleteMeetingDialog
```

Expected: PASS (6 tests).

- [ ] **Step 5: Add the delete hook**

Append to the end of `frontend/src/api/hooks/useMeetingTasks.ts`, after the `useReimportMeeting` function:

```ts
// --- Delete ---

export interface DeleteMeetingResponse {
  success: boolean;
  errorCode?: string;
}

export function useDeleteMeeting() {
  const qc = useQueryClient();
  return useMutation<DeleteMeetingResponse, Error, string>({
    mutationFn: async (transcriptId) =>
      fetchJson<DeleteMeetingResponse>(
        `/api/meeting-tasks/${encodeURIComponent(transcriptId)}`,
        { method: "DELETE", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, transcriptId) => {
      qc.removeQueries({ queryKey: MEETING_TASKS_KEYS.detail(transcriptId) });
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.list });
    },
  });
}
```

- [ ] **Step 6: Verify the build compiles**

```bash
cd frontend && CI=false npm run build
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/useMeetingTasks.ts \
        frontend/src/components/pages/automation/ConfirmDeleteMeetingDialog.tsx \
        frontend/src/components/pages/automation/__tests__/ConfirmDeleteMeetingDialog.test.tsx
git commit -m "feat: add meeting delete hook and confirmation dialog"
```

---

## Task 5: Wire the delete button into the meeting note detail page

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`
- Test: `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.delete.test.tsx`

**Interfaces:**
- Consumes: `useDeleteMeeting()` and `ConfirmDeleteMeetingDialog` from Task 4; the page's existing `isMeetingManager` flag (`hasPermission('anela.meetings.write')`, line 126) and `id` from `useParams`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing page test**

Create `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.delete.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import {
  useMeetingTaskDetail,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
  useUpdateTranscriptStatus,
  useAddProposedTask,
  useSubmitToTodo,
  useMeetingUsers,
  useReimportMeeting,
  useExplainMeetingSummary,
  useDeleteMeeting,
} from '../../../../api/hooks/useMeetingTasks';
import { useExplainSelection } from '../explain/useExplainSelection';
import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

// ---- Module mocks ----

jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({
  ...jest.requireActual('react-router-dom'),
  useNavigate: () => mockNavigate,
}));

jest.mock('../../../../api/hooks/useMeetingTasks');
let mockHasPermission: (perm: string) => boolean = () => false;
jest.mock('../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({
    permissions: [],
    isSuperUser: false,
    groups: [],
    isLoading: false,
    hasPermission: (p: string) => mockHasPermission(p),
  }),
}));
jest.mock('../../../../auth/useAuth', () => ({
  useAuth: () => ({ account: { username: 'me@anela.cz' } }),
}));
jest.mock('../explain/useExplainSelection');
jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));

// ---- Helpers ----

const noopMutation = { mutate: jest.fn(), mutateAsync: jest.fn(), isPending: false, isError: false, error: null, reset: jest.fn() };

function buildTranscript() {
  return {
    id: 'abc',
    subject: 'Schůzka s týmem',
    summary: 'AI summary text',
    rawTranscript: 'Speaker: Hello world',
    plaudRecordingId: 'plaud-1',
    plaudCreatedAt: '2026-05-19T10:00:00Z',
    status: 'Approved',
    receivedAt: '2026-05-19T10:00:00Z',
    reviewedAt: null,
    reviewedByUser: null,
    taskCount: 0,
    approvedTaskCount: 0,
    rejectedTaskCount: 0,
    tasks: [],
    participants: [],
    accessLevel: 'Private' as const,
    accessGrants: [],
  };
}

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/automation/meeting-tasks/abc']}>
        <Routes>
          <Route path="/automation/meeting-tasks/:id" element={<MeetingTaskDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function setupHooks(deleteMutation: Partial<typeof noopMutation> = {}) {
  (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript() } });
  (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
  (useUpdateTranscriptStatus as jest.Mock).mockReturnValue(noopMutation);
  (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
  (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
  (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
  (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
  (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
  (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
  (useDeleteMeeting as jest.Mock).mockReturnValue({ ...noopMutation, ...deleteMutation });
}

// ---- Tests ----

beforeEach(() => {
  jest.clearAllMocks();
  mockHasPermission = () => false;
});

describe('delete meeting button', () => {
  it('is hidden without the anela.meetings.write permission', () => {
    setupHooks();
    renderPage();
    expect(screen.queryByRole('button', { name: /^smazat$/i })).not.toBeInTheDocument();
  });

  it('is visible for a meeting manager', () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    setupHooks();
    renderPage();
    expect(screen.getByRole('button', { name: /^smazat$/i })).toBeInTheDocument();
  });

  it('opens the confirmation dialog instead of deleting immediately', () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));

    expect(screen.getByText('Smazat schůzku?')).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('deletes and navigates to the list when confirmed', async () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockResolvedValue({ success: true });
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
    fireEvent.click(screen.getAllByRole('button', { name: /^smazat$/i })[1]);

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith('abc'));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('/automation/meeting-tasks'));
  });

  it('keeps the dialog open and shows an error when deletion fails', async () => {
    mockHasPermission = (p) => p === 'anela.meetings.write';
    const mutateAsync = jest.fn().mockRejectedValue(new Error('API error: 500'));
    setupHooks({ mutateAsync });
    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /^smazat$/i }));
    fireEvent.click(screen.getAllByRole('button', { name: /^smazat$/i })[1]);

    await waitFor(() => expect(screen.getByText(/nezdařilo/i)).toBeInTheDocument());
    expect(screen.getByText('Smazat schůzku?')).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern=MeetingTaskDetailPage.delete
```

Expected: FAIL — `useDeleteMeeting is not a function` / the Smazat button is not found.

- [ ] **Step 3: Add the imports to the detail page**

In `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`:

Add `useNavigate` to the react-router-dom import (line 4):

```tsx
import { useParams, useNavigate } from "react-router-dom";
```

Add `Trash2` to the lucide-react import list (lines 5-8):

```tsx
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle, RefreshCw, Download, Undo2, Trash2,
} from "lucide-react";
```

Add `useDeleteMeeting` to the `useMeetingTasks` import list (keeping alphabetical order — it goes after `useAddProposedTask`):

```tsx
  useAddProposedTask,
  useDeleteMeeting,
  useExplainMeetingSummary,
```

Add the dialog import next to the other automation-page component imports (after the `MeetingReviewLeaveDialog` import on line 26):

```tsx
import ConfirmDeleteMeetingDialog from "./ConfirmDeleteMeetingDialog";
```

- [ ] **Step 4: Add the delete state and handler**

In the same file, after the `reimportError` state declaration (line 124), add:

```tsx
  const navigate = useNavigate();
  const deleteMeeting = useDeleteMeeting();
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
```

Then, after the `handleReimport` function (ends line 156), add:

```tsx
  const handleDelete = async () => {
    setDeleteError(null);
    try {
      await deleteMeeting.mutateAsync(id);
      // Navigate directly rather than through requestNavigation — the review leave
      // guard must not ask to "mark as reviewed" a meeting that no longer exists.
      navigate("/automation/meeting-tasks");
    } catch {
      setDeleteError("Smazání se nezdařilo. Zkuste to prosím znovu.");
    }
  };
```

- [ ] **Step 5: Add the button and render the dialog**

In the header action row, immediately after the closing `)}` of the manager-only "Spravovat přístup" block (lines 353-360) and before the row's closing `</div>`, add a second guarded block:

```tsx
          {isMeetingManager && (
            <button
              type="button"
              onClick={() => {
                setDeleteError(null);
                setDeleteDialogOpen(true);
              }}
              className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-red-300 dark:border-red-800 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20"
            >
              <Trash2 className="w-4 h-4 mr-1" aria-hidden="true" />
              Smazat
            </button>
          )}
```

Then, at the bottom of the component, immediately before `<MeetingReviewLeaveDialog {...dialogProps} />`, add:

```tsx
      {isMeetingManager && (
        <ConfirmDeleteMeetingDialog
          isOpen={deleteDialogOpen}
          subject={transcript.subject}
          isDeleting={deleteMeeting.isPending}
          error={deleteError}
          onConfirm={handleDelete}
          onCancel={() => setDeleteDialogOpen(false)}
        />
      )}
```

- [ ] **Step 6: Run the page tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false --testPathPattern=MeetingTaskDetailPage
```

Expected: PASS — the new delete tests plus the pre-existing `reviewState`, `filter` and `download` suites.

- [ ] **Step 7: Verify build and lint**

```bash
cd frontend && CI=false npm run build && npm run lint
```

Expected: both succeed with no new errors.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
        frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.delete.test.tsx
git commit -m "feat: add delete button with confirmation to meeting note detail"
```

---

## Task 6: Full-stack verification

**Files:** none created or modified unless a failure is found.

**Interfaces:**
- Consumes: everything from Tasks 1-5.
- Produces: a verified, buildable branch.

- [ ] **Step 1: Backend build, format check and full test run**

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false
```

Expected: build succeeds, format reports no changes, all tests pass. If `dotnet format --verify-no-changes` fails, run it without the flag and commit the formatting.

Note: an `AccessMatrixGen` crash in the test output is known, pre-existing noise and is not a failure.

- [ ] **Step 2: Frontend build, lint and full test run**

```bash
cd frontend && CI=false npm run build && npm run lint && CI=true npx react-scripts test --watchAll=false
```

Expected: all three succeed.

- [ ] **Step 3: Confirm the generated API client picked up the endpoint**

The OpenAPI TypeScript client is regenerated during the backend build. Confirm the delete operation is present:

```bash
grep -n "meetingTasks_Delete" frontend/src/api/generated/api-client.ts
```

Expected: at least one match. The hand-written hooks do not use the generated client, so no further wiring is needed — this only confirms the endpoint is exposed correctly. If there are changes to generated files, commit them.

- [ ] **Step 4: Commit any generated-file changes**

```bash
git status --short
# If frontend/src/api/generated/api-client.ts or access-matrix generated files changed:
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate api client for meeting delete endpoint"
```

If `git status` is clean, skip this step.

---

## Deployment note

The migration `AddDeletedPlaudRecordings` must be applied to the target database **before or together with** this code. Until it is, `DELETE /api/meeting-tasks/{id}` will fail with a database error, and `IngestPlaudRecordingHandler` will throw on every poll because it queries a table that does not exist.

```bash
dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
