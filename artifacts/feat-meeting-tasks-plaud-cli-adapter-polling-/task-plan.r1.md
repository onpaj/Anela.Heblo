# Meeting Tasks — Domain Model & EF Core Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the `MeetingTranscript` aggregate (with `ProposedTask` children) to the Domain layer, persist it via EF Core to PostgreSQL through a repository, and generate the schema migration — the foundation for the Meeting Task Validation Checkpoint epic.

**Architecture:** Two new domain entities (`MeetingTranscript`, `ProposedTask`) and two enums (`MeetingTranscriptStatus`, `ProposedTaskStatus`) under `Anela.Heblo.Domain/Features/MeetingTasks/`. EF Core `IEntityTypeConfiguration` classes and a repository implementation under `Anela.Heblo.Persistence/MeetingTasks/`, picked up by the existing `ApplyConfigurationsFromAssembly` scan. Repository registered in `PersistenceModule.AddPersistenceServices`. Single migration creates two `public.*` tables with FK cascade, one unique index on `PlaudRecordingId`, and three supporting indexes.

**Tech Stack:** .NET 8, C# 12 (nullable reference types), EF Core 8, Npgsql, PostgreSQL `public` schema. xUnit + FluentAssertions for tests (`UseInMemoryDatabase` for the lightweight unit tests added in Task 7).

---

## Branch & PR Targeting

- **Working branch:** `feat-meeting-tasks-domain-model-ef-core-persi` (already checked out in the worktree).
- **PR target branch:** `feat/meeting-task-validation-epic` (NOT `main`). The epic branch is the integration line for all sub-features in this epic.

---

## File Map

**Domain layer — create:**
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs` — aggregate-status enum.
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs` — child-status enum.
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs` — child entity (class).
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` — aggregate root (class).
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` — repository interface.

**Persistence layer — create:**
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` — EF config for parent.
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs` — EF config for child.
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` — EF repository.

**Persistence layer — modify:**
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add two `DbSet`s and a `using`.
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — register repository in DI.

**Persistence layer — generated (do not hand-edit beyond verification):**
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingTasksTables.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingTasksTables.Designer.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (auto-updated by `dotnet ef`)

**Tests — create:**
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs` — unit tests with `UseInMemoryDatabase`.

**Out of scope for this PR (do NOT create):**
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`
- Any DTOs (`MeetingTranscriptDto`, `ProposedTaskDto`)
- Any controllers, MediatR handlers, services
- Any UI files

These belong to the next subtask (first consumer of this persistence layer). The brief lists them in its file structure but the spec explicitly defers them.

---

### Task 1: Status enums

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs`

**Rationale:** Enums first — the entities reference them. Explicit integer values pin ordinal stability so future enum additions can't silently shift values, even though storage is via `HasConversion<string>()` (defense-in-depth).

- [ ] **Step 1: Create `MeetingTranscriptStatus.cs`**

Write file `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public enum MeetingTranscriptStatus
{
    PendingReview = 1,
    Approved = 2,
    PartiallyApproved = 3
}
```

- [ ] **Step 2: Create `ProposedTaskStatus.cs`**

Write file `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public enum ProposedTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
```

- [ ] **Step 3: Build the Domain project to confirm enums compile**

Run from worktree root:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs
git commit -m "feat: add MeetingTranscriptStatus and ProposedTaskStatus enums"
```

---

### Task 2: ProposedTask child entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`

**Rationale:** Define the child before the parent so the parent's `List<ProposedTask>` resolves. Class (not record) per project convention — entities have identity and mutable lifecycle, and OpenAPI generators mishandle record parameter order downstream.

- [ ] **Step 1: Create `ProposedTask.cs`**

Write file `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`:

```csharp
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

- [ ] **Step 2: Confirm Domain still builds (will fail on MeetingTranscript not found — that's expected, see next step)**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: Build FAILS with `CS0246: The type or namespace name 'MeetingTranscript' could not be found`. This is expected — the parent type is defined in Task 3.

- [ ] **Step 3: Do NOT commit yet**

The file references `MeetingTranscript` which doesn't exist. Hold the commit until Task 3 brings the project back to green.

---

### Task 3: MeetingTranscript aggregate root

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`

**Rationale:** Aggregate root holds the `Tasks` back-reference completing the bidirectional navigation EF Core needs to populate `Include(x => x.Tasks)`.

- [ ] **Step 1: Create `MeetingTranscript.cs`**

Write file `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`:

```csharp
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

- [ ] **Step 2: Build Domain project to confirm both entities now compile**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit both entities together**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs \
        backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs
git commit -m "feat: add MeetingTranscript and ProposedTask domain entities"
```

---

### Task 4: Repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`

**Rationale:** Domain owns the interface; Persistence will own the implementation. This keeps Application-layer consumers (next subtask) pointing only at Domain abstractions. Methods exactly match the spec FR-4 contract.

- [ ] **Step 1: Create `IMeetingTranscriptRepository.cs`**

Write file `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);

    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Build Domain project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs
git commit -m "feat: add IMeetingTranscriptRepository interface"
```

---

### Task 5: EF entity configurations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs`

**Rationale:** Configurations live in `Anela.Heblo.Persistence` and are picked up automatically by the existing `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` call in `ApplicationDbContext.OnModelCreating` — no explicit registration in `OnModelCreating` is required or permitted (would duplicate the scan). The global UTC↔Unspecified `DateTime` value converter in `OnModelCreating` already handles `DateTimeKind` translation; `AsUtcTimestamp()` adds the matching `HasColumnType("timestamp")` (without time zone). Default `HasConversion<string>()` stores enums as PascalCase strings (`"PendingReview"`, etc.) — resilient to enum reordering, readable in DB.

- [ ] **Step 1: Create `MeetingTranscriptConfiguration.cs`**

Write file `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs`:

```csharp
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

        builder.Property(x => x.PlaudRecordingId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PlaudCreatedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.Subject)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Summary)
            .IsRequired();

        builder.Property(x => x.RawTranscript)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(x => x.ReceivedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.ReviewedAt)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.ReviewedByUser)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.HasMany(x => x.Tasks)
            .WithOne(x => x.MeetingTranscript)
            .HasForeignKey(x => x.MeetingTranscriptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PlaudRecordingId)
            .IsUnique()
            .HasDatabaseName("UX_MeetingTranscripts_PlaudRecordingId");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_MeetingTranscripts_Status");

        builder.HasIndex(x => x.ReceivedAt)
            .HasDatabaseName("IX_MeetingTranscripts_ReceivedAt");
    }
}
```

- [ ] **Step 2: Create `ProposedTaskConfiguration.cs`**

Write file `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs`:

```csharp
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

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Description)
            .IsRequired();

        builder.Property(x => x.Assignee)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DueDate)
            .IsRequired(false)
            .AsUtcTimestamp();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(x => x.ExternalTaskId)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(x => x.IsManuallyAdded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => x.MeetingTranscriptId)
            .HasDatabaseName("IX_ProposedTasks_MeetingTranscriptId");
    }
}
```

- [ ] **Step 3: Build the Persistence project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` Note: the configurations are not yet "active" because `ApplicationDbContext` lacks the `DbSet`s — that's wired in Task 6.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs
git commit -m "feat: add EF Core configurations for MeetingTranscript and ProposedTask"
```

---

### Task 6: DbContext wiring

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

**Rationale:** Add `DbSet`s so EF Core surfaces the new entity types and so the assembly scan picks up the configurations. Place the DbSets next to the Marketing Invoices section per FR-6.

- [ ] **Step 1: Add `using` for Domain namespace**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`.

Insert this `using` directive alphabetically among existing `Anela.Heblo.Domain.Features.*` usings (between `Marketing` and `MarketingInvoices` is appropriate):

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
```

The full leading `using` block after edit should include:

```csharp
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Domain.Features.MeetingTasks;
```

- [ ] **Step 2: Add the two DbSets**

In the same file, locate the existing `// Marketing Invoices module` section (currently around line 101–103):

```csharp
    // Marketing Invoices module
    public DbSet<ImportedMarketingTransaction> ImportedMarketingTransactions { get; set; } = null!;
```

Immediately AFTER that section, add:

```csharp
    // Meeting Tasks module
    public DbSet<MeetingTranscript> MeetingTranscripts { get; set; } = null!;
    public DbSet<ProposedTask> ProposedTasks { get; set; } = null!;
```

- [ ] **Step 3: Do NOT modify `OnModelCreating`**

`ApplyConfigurationsFromAssembly` is already present (line ~127) and picks the new configurations up automatically. Adding any explicit `ApplyConfiguration` call here would duplicate the scan — leave it alone.

- [ ] **Step 4: Build the Persistence project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat: expose MeetingTranscripts and ProposedTasks DbSets on ApplicationDbContext"
```

---

### Task 7: Repository implementation + unit tests (TDD)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs`

**Rationale:** The repository is the only piece of this PR with behaviour beyond config; that behaviour deserves tests. We follow the project's existing repository-testing pattern (in-memory provider, FluentAssertions, one test per method behaviour). Tests are written first to drive a minimal correct implementation.

#### Step group A — write the failing tests

- [ ] **Step A1: Create test file with all five tests**

Write file `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MeetingTasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public class MeetingTranscriptRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MeetingTranscriptRepository _repository;

    public MeetingTranscriptRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new MeetingTranscriptRepository(_context);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTranscriptWithTasks_WhenExists()
    {
        // Arrange
        var transcript = BuildTranscript("plaud-1", taskCount: 2);
        _context.MeetingTranscripts.Add(transcript);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(transcript.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(transcript.Id);
        result.Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetListAsync_FiltersByStatus_PaginatesAndOrdersByPlaudCreatedAtDescending()
    {
        // Arrange — three transcripts, two PendingReview with distinct PlaudCreatedAt, one Approved
        var older = BuildTranscript("plaud-old", taskCount: 0);
        older.PlaudCreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        older.Status = MeetingTranscriptStatus.PendingReview;

        var newer = BuildTranscript("plaud-new", taskCount: 1);
        newer.PlaudCreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        newer.Status = MeetingTranscriptStatus.PendingReview;

        var approved = BuildTranscript("plaud-approved", taskCount: 0);
        approved.PlaudCreatedAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        approved.Status = MeetingTranscriptStatus.Approved;

        _context.MeetingTranscripts.AddRange(older, newer, approved);
        await _context.SaveChangesAsync();

        // Act
        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: MeetingTranscriptStatus.PendingReview,
            page: 1,
            pageSize: 10);

        // Assert
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].PlaudRecordingId.Should().Be("plaud-new");   // newer first
        items[1].PlaudRecordingId.Should().Be("plaud-old");
        items[0].Tasks.Should().HaveCount(1);                 // Tasks eagerly loaded
    }

    [Fact]
    public async Task GetListAsync_WithoutStatusFilter_ReturnsAll()
    {
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-a", 0));
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-b", 0));
        await _context.SaveChangesAsync();

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter: null, page: 1, pageSize: 10);

        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExistsByPlaudIdAsync_ReturnsTrueWhenPresent_FalseOtherwise()
    {
        _context.MeetingTranscripts.Add(BuildTranscript("plaud-x", 0));
        await _context.SaveChangesAsync();

        (await _repository.ExistsByPlaudIdAsync("plaud-x")).Should().BeTrue();
        (await _repository.ExistsByPlaudIdAsync("plaud-missing")).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_PersistsAggregateAndChildren_AfterSaveChanges()
    {
        var transcript = BuildTranscript("plaud-new", taskCount: 3);

        await _repository.AddAsync(transcript);
        await _repository.SaveChangesAsync();

        var reloaded = await _context.MeetingTranscripts
            .Include(t => t.Tasks)
            .FirstAsync(t => t.PlaudRecordingId == "plaud-new");

        reloaded.Tasks.Should().HaveCount(3);
    }

    private static MeetingTranscript BuildTranscript(string plaudId, int taskCount)
    {
        var now = DateTime.UtcNow;
        var transcript = new MeetingTranscript
        {
            Id = Guid.NewGuid(),
            PlaudRecordingId = plaudId,
            PlaudCreatedAt = now,
            Subject = $"Subject {plaudId}",
            Summary = $"Summary {plaudId}",
            RawTranscript = $"Raw {plaudId}",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = now
        };

        for (var i = 0; i < taskCount; i++)
        {
            transcript.Tasks.Add(new ProposedTask
            {
                Id = Guid.NewGuid(),
                Title = $"Task {i}",
                Description = $"Description {i}",
                Assignee = "alice",
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false
            });
        }

        return transcript;
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step A2: Run the tests to confirm they fail (no repository class yet)**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests" \
    --no-build 2>&1 | tail -40
```

Expected: BUILD FAILS with `CS0246: The type or namespace name 'MeetingTranscriptRepository' could not be found`. This confirms RED — the production code does not exist yet.

#### Step group B — write minimal repository

- [ ] **Step B1: Create `MeetingTranscriptRepository.cs`**

Write file `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`:

```csharp
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

    public Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .Include(x => x.Tasks)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.MeetingTranscripts.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(x => x.Status == statusFilter.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(x => x.Tasks)
            .OrderByDescending(x => x.PlaudCreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default)
    {
        return _context.MeetingTranscripts
            .AnyAsync(x => x.PlaudRecordingId == plaudRecordingId, ct);
    }

    public async Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default)
    {
        await _context.MeetingTranscripts.AddAsync(transcript, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step B2: Build the Persistence project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step B3: Run the tests — expect GREEN**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests" 2>&1 | tail -20
```

Expected: `Passed!  - Failed: 0, Passed: 6, Skipped: 0` (or similar, with all 6 tests green).

- [ ] **Step B4: Commit repository and tests together**

```bash
git add backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs
git commit -m "feat: add MeetingTranscriptRepository with unit tests"
```

---

### Task 8: DI registration in `PersistenceModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

**Rationale:** This codebase does NOT auto-register repositories — every repository is added explicitly in `PersistenceModule.AddPersistenceServices`. Without this line the feature compiles but `IMeetingTranscriptRepository` cannot resolve at runtime when the first consumer arrives. Per arch-review Amendment 1, this is mandatory.

- [ ] **Step 1: Add `using` for the Persistence implementation namespace**

Open `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`.

In the existing `using` block, add (alphabetical placement, between `KnowledgeBase` and `Xcc`):

```csharp
using Anela.Heblo.Persistence.MeetingTasks;
```

Also add the Domain interface namespace if not already present (between `KnowledgeBase` and `Leaflet` in the `Domain.Features.*` block):

```csharp
using Anela.Heblo.Domain.Features.MeetingTasks;
```

- [ ] **Step 2: Register the repository**

Locate the existing block (around line 125–126):

```csharp
        // KnowledgeBase repositories
        services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
```

Immediately AFTER that line, add:

```csharp

        // Meeting Tasks repositories
        services.AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>();
```

- [ ] **Step 3: Build the Persistence project**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat: register IMeetingTranscriptRepository in PersistenceModule"
```

---

### Task 9: Generate EF Core migration

**Files (generated by `dotnet ef`):**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingTasksTables.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingTasksTables.Designer.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

**Rationale:** A single migration that creates both tables, the FK with cascade delete, the unique index on `PlaudRecordingId`, and the three supporting indexes. The Persistence project has `DesignTimeDbContextFactory.cs`, so `dotnet ef` can run from the API project (default startup) without extra flags.

- [ ] **Step 1: Verify EF tooling is installed**

Run:

```bash
dotnet ef --version
```

Expected: prints a version (e.g. `Entity Framework Core .NET Command-line Tools 8.x.x`). If not, install with `dotnet tool install --global dotnet-ef`.

- [ ] **Step 2: Generate the migration from the API project directory**

Run:

```bash
cd backend/src/Anela.Heblo.API && \
dotnet ef migrations add AddMeetingTasksTables \
    --project ../Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
    --output-dir Migrations
```

Expected: two files generated under `backend/src/Anela.Heblo.Persistence/Migrations/` — `<timestamp>_AddMeetingTasksTables.cs` and `<timestamp>_AddMeetingTasksTables.Designer.cs`. `ApplicationDbContextModelSnapshot.cs` is updated.

(Return to worktree root with `cd ../../..` afterward if needed for subsequent commands.)

- [ ] **Step 3: Open the generated migration `Up` method and verify all five anchors**

Read `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMeetingTasksTables.cs` and confirm the `Up` method contains:

1. `CreateTable("MeetingTranscripts", schema: "public", ...)` with all 10 columns (`Id`, `PlaudRecordingId`, `PlaudCreatedAt`, `Subject`, `Summary`, `RawTranscript`, `Status`, `ReceivedAt`, `ReviewedAt`, `ReviewedByUser`) using `timestamp` (without time zone) for the DateTime columns, `character varying(50)` for `Status`, `character varying(200)` for `PlaudRecordingId` and `ReviewedByUser`, `character varying(500)` for `Subject`, and `text` for `Summary` and `RawTranscript`.

2. `CreateTable("ProposedTasks", schema: "public", ...)` with all 9 columns plus the FK definition `ForeignKey("FK_ProposedTasks_MeetingTranscripts_MeetingTranscriptId", ..., onDelete: ReferentialAction.Cascade)`. `IsManuallyAdded` should have `defaultValue: false`.

3. `CreateIndex("UX_MeetingTranscripts_PlaudRecordingId", schema: "public", table: "MeetingTranscripts", column: "PlaudRecordingId", unique: true)`.

4. `CreateIndex("IX_MeetingTranscripts_Status", ...)` and `CreateIndex("IX_MeetingTranscripts_ReceivedAt", ...)` — neither unique.

5. `CreateIndex("IX_ProposedTasks_MeetingTranscriptId", schema: "public", table: "ProposedTasks", column: "MeetingTranscriptId")` — not unique.

If any anchor is missing or different, STOP and revisit the relevant `Configuration` class in Task 5 — do NOT hand-patch the migration.

- [ ] **Step 4: Verify the `Down` method drops both tables**

Confirm the `Down` method calls `DropTable("ProposedTasks", schema: "public")` first, then `DropTable("MeetingTranscripts", schema: "public")` (child before parent so the FK doesn't fight us).

- [ ] **Step 5: Build the full solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Run the new tests one more time**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests" 2>&1 | tail -10
```

Expected: `Passed!` — all 6 tests still green.

- [ ] **Step 7: Commit the migration**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add EF Core migration AddMeetingTasksTables"
```

---

### Task 10: Final validation gate

**Files:** none (validation only).

**Rationale:** Per project CLAUDE.md, completion requires `dotnet build` + `dotnet format` clean, plus touched tests green. This task is the explicit gate before declaring done.

- [ ] **Step 1: Full backend build**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded. 0 Error(s).` No new warnings introduced beyond the pre-existing baseline.

- [ ] **Step 2: Run `dotnet format` and confirm no diff on the new files**

Run:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-meeting-tasks-domain-model-ef-core-persi && \
dotnet format backend/Anela.Heblo.sln --verify-no-changes 2>&1 | tail -20
```

Expected: command exits 0 (no diffs). If diffs are reported, run `dotnet format backend/Anela.Heblo.sln` without `--verify-no-changes`, inspect with `git diff`, commit the formatting fixes in a separate `chore: dotnet format` commit, then re-run the verify command.

- [ ] **Step 3: Run the full test project once**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | tail -15
```

Expected: all pre-existing tests plus the 6 new ones pass. No regressions.

- [ ] **Step 4: Push the branch**

```bash
git push -u origin feat-meeting-tasks-domain-model-ef-core-persi
```

Expected: branch pushed; GitHub prints the URL for opening a PR.

- [ ] **Step 5: Open the PR against `feat/meeting-task-validation-epic`**

Use `gh pr create` (NOT MCP GitHub tools — per project rule). PR base MUST be `feat/meeting-task-validation-epic`, not `main`:

```bash
gh pr create --base feat/meeting-task-validation-epic --title "feat: meeting tasks — domain model & EF Core persistence" --body "$(cat <<'EOF'
## Summary
- Add `MeetingTranscript` aggregate and `ProposedTask` child entity under `Domain/Features/MeetingTasks/`.
- Add EF Core configurations and `MeetingTranscriptRepository`; register in `PersistenceModule`.
- Generate `AddMeetingTasksTables` migration creating `public.MeetingTranscripts` and `public.ProposedTasks` with FK cascade, unique index on `PlaudRecordingId`, and supporting indexes on `Status`, `ReceivedAt`, and `MeetingTranscriptId`.
- Foundation for the Meeting Task Validation Checkpoint epic — no API/UI in this PR.

## Test plan
- [ ] `dotnet build backend/Anela.Heblo.sln` is clean.
- [ ] `dotnet format backend/Anela.Heblo.sln --verify-no-changes` is clean.
- [ ] `MeetingTranscriptRepositoryTests` (6 tests) pass under `Anela.Heblo.Tests`.
- [ ] Migration `Up` creates both tables, all four indexes, and the FK with cascade delete.
- [ ] Migration `Down` cleanly drops both tables (child first).
- [ ] Apply migration against a local Postgres dev instance and confirm tables/indexes exist (manual — DB migrations are manual per project facts).
EOF
)"
```

Expected: PR URL printed. Do NOT auto-merge.

---

## Self-Review Notes

**Spec coverage:**
- FR-1 MeetingTranscript aggregate root → Task 3.
- FR-2 ProposedTask child entity → Task 2.
- FR-3 Status enumerations → Task 1.
- FR-4 Repository interface → Task 4.
- FR-5 EF Core entity configurations → Task 5 (every property/index/relationship enumerated).
- FR-6 DbContext integration → Task 6 (DbSets added between Marketing Invoices and the existing trailing modules, `using` added, no `OnModelCreating` edit because the assembly scan already exists).
- FR-7 Repository implementation → Task 7.
- FR-8 Database migration → Task 9.
- FR-9 Build & format validation → Task 10.

**Arch-review amendments coverage:**
- Amendment 1 (explicit DI registration) → Task 8.
- Amendment 2 (drop "if present" hedge in FR-6) → respected in Task 6 Step 3.
- Amendment 3 (no `MeetingTasksModule` / `MeetingTasksOptions` / DTOs in this PR) → called out in the file map's "Out of scope" subsection.
- Amendment 4 (migration command working directory) → Task 9 Step 2 uses `cd backend/src/Anela.Heblo.API` and the canonical command.
- Amendment 5 (status enum string casing — default PascalCase) → applied via default `HasConversion<string>()` in Task 5 with no custom converter.

**Type consistency:**
- Method names in the test file (`AddAsync`, `SaveChangesAsync`, `GetByIdAsync`, `GetListAsync`, `ExistsByPlaudIdAsync`) match Task 4's interface, Task 7's implementation, and the spec's contract.
- Entity property names match between Task 2/3 (entities) → Task 5 (configurations) → Task 7 (tests).
- `MeetingTranscriptStatus` and `ProposedTaskStatus` referenced consistently across Tasks 1, 3, 5, 7.

**Placeholder scan:** No "TBD", "TODO", "implement later", "add appropriate error handling", or "similar to Task N" — every code-bearing step contains the exact code.

**Risk handling:**
- Race on dedup (arch-review): documented; the unique index in Task 5 + the migration in Task 9 provide the defence-in-depth. Catching `DbUpdateException` for code 23505 belongs to the ingestion handler subtask (next PR).
- `RawTranscript` size on full-row reads: acknowledged; out of scope here. A summary-projection method is left for the list-UI consumer subtask.
- Migration designer file churn: standard EF Core flow; nothing to do.
