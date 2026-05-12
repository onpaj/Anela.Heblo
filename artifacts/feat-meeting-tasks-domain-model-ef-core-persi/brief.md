# Subtask 1: Domain Model & EF Core Persistence

**Parent Epic:** Meeting Task Validation Checkpoint

## File Structure

### Backend — Domain
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` — aggregate root entity
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs` — child entity
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs` — enum
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs` — enum
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` — repository interface

### Backend — Application
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — DI registration
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` — config (TODO list name)
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs` — DTOs
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/ProposedTaskDto.cs` — DTOs

### Backend — Persistence
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` — EF config
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs` — EF config
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` — repository

### Migrations
- EF Core migration for `MeetingTranscripts` and `ProposedTasks` tables

---

## Task 1: Domain Model

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`

- [ ] **Step 1: Create MeetingTranscriptStatus enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public enum MeetingTranscriptStatus
{
    PendingReview = 1,
    Approved = 2,
    PartiallyApproved = 3
}
```

- [ ] **Step 2: Create ProposedTaskStatus enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public enum ProposedTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
```

- [ ] **Step 3: Create MeetingTranscript entity**

> **Note:** `SourceEmail` was removed. The recording is now identified by `PlaudRecordingId` (unique, used for deduplication). `RawTranscript` stores the full transcript text. `PlaudCreatedAt` records when the recording was created in Plaud.

```csharp
// backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class MeetingTranscript
{
    public Guid Id { get; set; }
    public string PlaudRecordingId { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
    public string Subject { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string RawTranscript { get; set; } = null!;
    public MeetingTranscriptStatus Status { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUser { get; set; }

    public List<ProposedTask> Tasks { get; set; } = new();
}
```

- [ ] **Step 4: Create ProposedTask entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class ProposedTask
{
    public Guid Id { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
    public ProposedTaskStatus Status { get; set; }
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}
```

- [ ] **Step 5: Create repository interface**

> **Note:** `FindDuplicateAsync` (5-minute window dedup) is replaced by `ExistsByPlaudIdAsync` — dedup is now by unique Plaud recording ID.

```csharp
// backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter, int page, int pageSize, CancellationToken ct = default);
    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);
    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Domain/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/
git commit -m "feat(meeting-tasks): add domain model — MeetingTranscript, ProposedTask, enums, repository interface"
```

---

## Task 2: EF Core Configuration & Migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add DbSets

- [ ] **Step 1: Create MeetingTranscriptConfiguration**

```csharp
// backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingTranscriptConfiguration : IEntityTypeConfiguration<MeetingTranscript>
{
    public void Configure(EntityTypeBuilder<MeetingTranscript> builder)
    {
        builder.ToTable("MeetingTranscripts", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlaudRecordingId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PlaudCreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Summary).IsRequired();
        builder.Property(x => x.RawTranscript).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ReviewedAt).AsUtcTimestamp();
        builder.Property(x => x.ReviewedByUser).HasMaxLength(200);

        builder.HasMany(x => x.Tasks)
            .WithOne(x => x.MeetingTranscript)
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PlaudRecordingId).IsUnique()
            .HasDatabaseName("UX_MeetingTranscripts_PlaudRecordingId");
        builder.HasIndex(x => x.Status).HasDatabaseName("IX_MeetingTranscripts_Status");
        builder.HasIndex(x => x.ReceivedAt).HasDatabaseName("IX_MeetingTranscripts_ReceivedAt");
    }
}
```

- [ ] **Step 2: Create ProposedTaskConfiguration**

```csharp
// backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class ProposedTaskConfiguration : IEntityTypeConfiguration<ProposedTask>
{
    public void Configure(EntityTypeBuilder<ProposedTask> builder)
    {
        builder.ToTable("ProposedTasks", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).IsRequired();
        builder.Property(x => x.Assignee).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DueDate).AsUtcTimestamp();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalTaskId).HasMaxLength(200);
        builder.Property(x => x.IsManuallyAdded).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => x.MeetingTranscriptId).HasDatabaseName("IX_ProposedTasks_MeetingTranscriptId");
    }
}
```

- [ ] **Step 3: Add DbSets to ApplicationDbContext**

Add after the Marketing Invoices module section in `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`:

```csharp
// Meeting Tasks module
public DbSet<MeetingTranscript> MeetingTranscripts { get; set; } = null!;
public DbSet<ProposedTask> ProposedTasks { get; set; } = null!;
```

Add the using: `using Anela.Heblo.Domain.Features.MeetingTasks;`

- [ ] **Step 4: Create MeetingTranscriptRepository**

```csharp
// backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.MeetingTasks;

public class MeetingTranscriptRepository : IMeetingTranscriptRepository
{
    private readonly ApplicationDbContext _context;

    public MeetingTranscriptRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.MeetingTranscripts
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.MeetingTranscripts
            .Include(x => x.Tasks)
            .AsQueryable();

        if (statusFilter.HasValue)
            query = query.Where(x => x.Status == statusFilter.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.PlaudCreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default)
    {
        return await _context.MeetingTranscripts
            .AnyAsync(x => x.PlaudRecordingId == plaudRecordingId, ct);
    }

    public async Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default)
    {
        await _context.MeetingTranscripts.AddAsync(transcript, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/`
Expected: Build succeeded

- [ ] **Step 6: Generate EF Core migration**

Run from `backend/src/Anela.Heblo.API/`:
```bash
dotnet ef migrations add AddMeetingTasksTables --project ../Anela.Heblo.Persistence/
```
Expected: Migration created in `backend/src/Anela.Heblo.Persistence/Migrations/`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/MeetingTasks/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(meeting-tasks): add EF Core configuration, repository, and migration"
```

---

> **Integration:** Create your feature branch from `feat/meeting-task-validation-epic`. When done, open a PR targeting `feat/meeting-task-validation-epic` (not `main`).
