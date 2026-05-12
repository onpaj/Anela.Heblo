# Specification: Meeting Tasks — Domain Model & EF Core Persistence

## Summary
Establish the foundational data layer for the Meeting Task Validation Checkpoint epic: a `MeetingTranscript` aggregate with `ProposedTask` children, persisted via EF Core to PostgreSQL. This subtask delivers domain entities, repository interface and implementation, EF configurations, and a migration — no API endpoints or business logic beyond data access. It is the prerequisite for downstream subtasks (Plaud ingestion, review UI, external task creation).

## Background
The parent epic introduces a human-in-the-loop validation step between Plaud meeting recordings and external task creation. Plaud produces transcripts and AI-extracted task proposals; users must review and approve these before they become real tasks in the team's TODO system. This subtask creates the persistence backbone that holds those transcripts and their proposed tasks through the review lifecycle.

Plaud recordings are uniquely identified by `PlaudRecordingId`, which provides natural deduplication (replacing an earlier design that used a 5-minute time window over `SourceEmail`). Each transcript carries its raw text, AI-generated summary, and a list of proposed tasks; status fields track review progress.

## Functional Requirements

### FR-1: MeetingTranscript Aggregate Root
The domain layer must define `MeetingTranscript` as an aggregate root capturing one Plaud recording and its review state.

**Acceptance criteria:**
- Class defined at `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs`.
- Properties: `Id` (Guid), `PlaudRecordingId` (string, required), `PlaudCreatedAt` (DateTime), `Subject` (string, required), `Summary` (string, required), `RawTranscript` (string, required), `Status` (`MeetingTranscriptStatus`), `ReceivedAt` (DateTime), `ReviewedAt` (DateTime?), `ReviewedByUser` (string?), `Tasks` (`List<ProposedTask>`).
- Required string fields use `null!` initializer (non-nullable reference types enabled).
- Class is not a C# record (per project DTO/entity convention).

### FR-2: ProposedTask Child Entity
The domain layer must define `ProposedTask` as a child entity owned by `MeetingTranscript`.

**Acceptance criteria:**
- Class defined at `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs`.
- Properties: `Id` (Guid), `MeetingTranscriptId` (Guid FK), `MeetingTranscript` (navigation), `Title` (string, required), `Description` (string, required), `Assignee` (string, required), `DueDate` (DateTime?), `Status` (`ProposedTaskStatus`), `ExternalTaskId` (string?), `IsManuallyAdded` (bool).
- Bidirectional navigation with parent (back-reference on `MeetingTranscript.Tasks`).

### FR-3: Status Enumerations
Two enums must encode the lifecycle states for transcripts and proposed tasks.

**Acceptance criteria:**
- `MeetingTranscriptStatus` with values `PendingReview = 1`, `Approved = 2`, `PartiallyApproved = 3`.
- `ProposedTaskStatus` with values `Pending = 1`, `Approved = 2`, `Rejected = 3`.
- Both located under `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/`.
- Explicit integer values assigned for ordinal stability.

### FR-4: Repository Interface
A repository abstraction must expose data access operations needed by downstream application services.

**Acceptance criteria:**
- Interface `IMeetingTranscriptRepository` at `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs`.
- Methods:
  - `GetByIdAsync(Guid id, CancellationToken)` returns `Task<MeetingTranscript?>` with `Tasks` eagerly loaded.
  - `GetListAsync(MeetingTranscriptStatus? statusFilter, int page, int pageSize, CancellationToken)` returns paginated tuple `(List<MeetingTranscript> Items, int TotalCount)`, ordered by `PlaudCreatedAt` descending, with `Tasks` eagerly loaded.
  - `ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken)` returns `Task<bool>` for deduplication checks.
  - `AddAsync(MeetingTranscript, CancellationToken)`.
  - `SaveChangesAsync(CancellationToken)`.
- All methods accept an optional `CancellationToken` with default value.

### FR-5: EF Core Entity Configurations
EF configurations must define table mappings, column constraints, indexes, and relationships.

**Acceptance criteria:**
- `MeetingTranscriptConfiguration` at `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` configures table `MeetingTranscripts` in schema `public`.
  - `PlaudRecordingId`: max length 200, required.
  - `Subject`: max length 500, required.
  - `Summary`, `RawTranscript`: required, no length cap (TEXT).
  - `Status`: stored as string via `HasConversion<string>()`, max length 50, required.
  - `PlaudCreatedAt`, `ReceivedAt`: required, mapped as UTC timestamp via `AsUtcTimestamp()` extension.
  - `ReviewedAt`: optional, UTC timestamp.
  - `ReviewedByUser`: max length 200, optional.
  - One-to-many to `ProposedTask` with cascade delete.
  - Unique index `UX_MeetingTranscripts_PlaudRecordingId` on `PlaudRecordingId`.
  - Non-unique indexes `IX_MeetingTranscripts_Status` and `IX_MeetingTranscripts_ReceivedAt`.
- `ProposedTaskConfiguration` at `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs` configures table `ProposedTasks` in schema `public`.
  - `Title`: max length 500, required.
  - `Description`: required, no length cap.
  - `Assignee`: max length 200, required.
  - `DueDate`: optional, UTC timestamp.
  - `Status`: stored as string, max length 50, required.
  - `ExternalTaskId`: max length 200, optional.
  - `IsManuallyAdded`: required, default `false`.
  - Index `IX_ProposedTasks_MeetingTranscriptId` on FK.

### FR-6: DbContext Integration
`ApplicationDbContext` must expose DbSets for the new entities.

**Acceptance criteria:**
- Added `DbSet<MeetingTranscript> MeetingTranscripts` and `DbSet<ProposedTask> ProposedTasks` in `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`.
- Placement: immediately after the Marketing Invoices module DbSet section.
- `using Anela.Heblo.Domain.Features.MeetingTasks;` added.
- Configurations auto-applied via existing `ApplyConfigurationsFromAssembly` (if present) or explicit registration consistent with the project's existing pattern.

### FR-7: Repository Implementation
A concrete EF Core repository must implement the interface.

**Acceptance criteria:**
- `MeetingTranscriptRepository` at `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs`.
- Constructor takes `ApplicationDbContext` via DI.
- `GetByIdAsync` and `GetListAsync` use `Include(x => x.Tasks)`.
- `GetListAsync` applies status filter conditionally, paginates with `Skip`/`Take`, returns total count from filtered query.
- `ExistsByPlaudIdAsync` uses `AnyAsync` against `PlaudRecordingId`.
- `AddAsync` calls `DbSet.AddAsync` without saving.
- `SaveChangesAsync` delegates to `_context.SaveChangesAsync`.

### FR-8: Database Migration
A single EF Core migration must create both tables with all configured constraints and indexes.

**Acceptance criteria:**
- Migration named `AddMeetingTasksTables` generated against `Anela.Heblo.Persistence`.
- Produces `MeetingTranscripts` and `ProposedTasks` tables in `public` schema with all columns, FK, unique and non-unique indexes from FR-5.
- Up migration creates schema additions; Down migration cleanly drops both tables.
- Migration files placed in `backend/src/Anela.Heblo.Persistence/Migrations/`.

### FR-9: Build & Format Validation
The full backend must build cleanly with the new code and pass formatting checks.

**Acceptance criteria:**
- `dotnet build` succeeds for `Anela.Heblo.Domain`, `Anela.Heblo.Persistence`, and the full solution.
- `dotnet format` produces no diffs against the new files.
- No new warnings introduced beyond pre-existing baseline.

## Non-Functional Requirements

### NFR-1: Performance
- Repository queries must use indexed columns for filters: `Status` (filtered list), `PlaudRecordingId` (dedup), `MeetingTranscriptId` (child fetch).
- `GetListAsync` returns paginated results — no unbounded fetches.
- Eager loading of `Tasks` via `Include` is acceptable given the expected small fan-out (≤ ~20 tasks per transcript).

### NFR-2: Security
- No authentication/authorization concerns at the persistence layer; access control belongs in the upstream application/API layer (out of scope here).
- `RawTranscript` may contain sensitive meeting content; the data layer stores it as plain TEXT in the same Postgres instance as other business data (consistent with project conventions — no separate encryption).
- No PII fields beyond `ReviewedByUser` (a username/email string, treated like other audit fields in the project).

### NFR-3: Data Integrity
- Unique constraint on `PlaudRecordingId` guarantees idempotent ingestion at the database level (defense in depth even if application-level dedup check is skipped).
- FK with `OnDelete(Cascade)` ensures orphaned `ProposedTask` rows cannot exist.
- Status enums stored as strings (not ints) for readability in DB and resilience to enum reordering.

### NFR-4: Maintainability
- Entity files ≤ 50 lines each; configurations ≤ 50 lines each.
- Naming follows existing project conventions (`Features/<Module>/` layout, `public` schema, `AsUtcTimestamp()` extension for timestamps, `HasConversion<string>()` for status enums).
- Repository follows existing project patterns (constructor injection of `ApplicationDbContext`, async-only methods, explicit `SaveChangesAsync`).

### NFR-5: Time Zone Handling
- All `DateTime` columns persisted as UTC via the project's `AsUtcTimestamp()` extension to avoid Postgres `timestamp with time zone` conversion surprises.

## Data Model

```
MeetingTranscript (aggregate root)
├── Id: Guid (PK)
├── PlaudRecordingId: string(200) — UNIQUE
├── PlaudCreatedAt: timestamp UTC
├── Subject: string(500)
├── Summary: text
├── RawTranscript: text
├── Status: string(50) [PendingReview | Approved | PartiallyApproved]
├── ReceivedAt: timestamp UTC
├── ReviewedAt: timestamp UTC?
├── ReviewedByUser: string(200)?
└── Tasks: 1..N → ProposedTask (cascade delete)

ProposedTask
├── Id: Guid (PK)
├── MeetingTranscriptId: Guid (FK → MeetingTranscript.Id) — INDEXED
├── Title: string(500)
├── Description: text
├── Assignee: string(200)
├── DueDate: timestamp UTC?
├── Status: string(50) [Pending | Approved | Rejected]
├── ExternalTaskId: string(200)?
└── IsManuallyAdded: bool (default false)
```

**Indexes:**
- `UX_MeetingTranscripts_PlaudRecordingId` — unique, supports dedup.
- `IX_MeetingTranscripts_Status` — supports filtered list queries.
- `IX_MeetingTranscripts_ReceivedAt` — supports time-window queries.
- `IX_ProposedTasks_MeetingTranscriptId` — supports child fetch.

## API / Interface Design

No public HTTP endpoints are introduced in this subtask. The contract surface is the C# repository interface consumed by downstream application-layer code:

```csharp
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

DI registration of `IMeetingTranscriptRepository → MeetingTranscriptRepository` and the `MeetingTasksModule`/`MeetingTasksOptions` skeleton listed in the brief's file structure are noted as in-scope wiring tasks if not already covered by convention-based registration; otherwise, they are deferred to the next subtask that introduces the first consumer.

## Dependencies

**Internal:**
- `Anela.Heblo.Persistence.Extensions.AsUtcTimestamp()` — existing extension for UTC timestamp configuration.
- `ApplicationDbContext` — existing root DbContext.
- Existing project conventions for `Features/<Module>/` organization.

**External:**
- `Microsoft.EntityFrameworkCore` (already referenced).
- `Microsoft.EntityFrameworkCore.Design` for migrations (already referenced).
- Npgsql provider (already in use).

**Branching:**
- This work branches from `feat/meeting-task-validation-epic` and targets that branch in its PR — not `main`.

## Out of Scope

- Plaud webhook/ingestion handler (separate subtask).
- AI summarization or task extraction (separate subtask).
- Review UI and approval workflow endpoints (separate subtask).
- External TODO system integration (Microsoft To Do / Tasks) for creating approved tasks (separate subtask).
- DTOs (`MeetingTranscriptDto`, `ProposedTaskDto`) and module registration (`MeetingTasksModule`, `MeetingTasksOptions`) — listed in brief's file structure but not in the step-by-step tasks; treated as scaffolding for the next subtask unless required to compile.
- Authorization rules around who may review transcripts.
- Database migration execution in any environment (migrations are manual per project facts).
- Unit/integration tests — the brief defines only build verification; deeper test coverage belongs to the consumer subtasks where behavior exists to assert.
- Performance tuning beyond the indexes specified.

## Open Questions

None.

## Status: COMPLETE