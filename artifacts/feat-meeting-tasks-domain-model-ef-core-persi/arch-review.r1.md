# Architecture Review: Meeting Tasks — Domain Model & EF Core Persistence

## Skip Design: true

Backend-only persistence work — no UI components or visual changes introduced. The feature delivers entities, EF configurations, a repository, and a migration. UI for review/approval lives in a downstream subtask.

## Architectural Fit Assessment

The proposal aligns cleanly with established project patterns. Every convention the spec leans on already exists in the codebase:

- **Vertical Slice layout under `Features/<Module>/`** — matches `Domain/Features/KnowledgeBase`, `Domain/Features/Marketing`, etc.
- **Persistence sibling folder `Persistence/MeetingTasks/`** — mirrors `Persistence/KnowledgeBase/`, `Persistence/Marketing/`.
- **`AsUtcTimestamp()` extension** — exists at `Persistence/Extensions/DateTimeConfigurationExtensions.cs`, sets column type `timestamp` (without time zone).
- **`HasConversion<string>()` for status enums** — used by `KnowledgeBaseDocumentConfiguration` and other modules.
- **Auto-registration of configurations** — `ApplicationDbContext.OnModelCreating` already calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)`, so new `IEntityTypeConfiguration<>` classes are picked up with zero registration code.
- **Global UTC DateTime value converter** — `OnModelCreating` installs a value converter for every `DateTime`/`DateTime?` property converting UTC↔Unspecified, so the spec's expectations about UTC handling are already enforced globally; `AsUtcTimestamp()` adds the column-type half.
- **Public schema** — consistent with the `MoveTablesFromDboToPublicSchema` migration baseline.
- **Repository style** — bespoke methods per aggregate (e.g. `KnowledgeBaseRepository`), constructor injection of `ApplicationDbContext`, explicit `SaveChangesAsync`. The spec follows this exactly.

Main integration point: `ApplicationDbContext` (add two `DbSet`s) and `PersistenceModule.AddPersistenceServices` (register the new repository — see Specification Amendments). No cross-module references introduced; module boundary is clean.

One non-fit to flag (informational, not blocking): the project's docs state Phase 2 will give each module its own `DbContext`. The new entities join the single shared context in line with Phase 1 — this is consistent with every other module today, no special action needed.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Domain  (Features/MeetingTasks/)                         │
│   MeetingTranscript (aggregate root, class)                          │
│   ProposedTask      (child entity, class)                            │
│   MeetingTranscriptStatus, ProposedTaskStatus (enums, explicit int)  │
│   IMeetingTranscriptRepository (interface)                           │
└──────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ implements
                              │
┌──────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Persistence  (MeetingTasks/)                             │
│   MeetingTranscriptConfiguration  : IEntityTypeConfiguration<...>    │
│   ProposedTaskConfiguration       : IEntityTypeConfiguration<...>    │
│   MeetingTranscriptRepository     : IMeetingTranscriptRepository     │
│                                                                       │
│ ApplicationDbContext  (modified)                                     │
│   + DbSet<MeetingTranscript> MeetingTranscripts                      │
│   + DbSet<ProposedTask>      ProposedTasks                           │
│   (configs auto-picked by ApplyConfigurationsFromAssembly)           │
│                                                                       │
│ PersistenceModule.AddPersistenceServices  (modified)                 │
│   + services.AddScoped<IMeetingTranscriptRepository,                 │
│                        MeetingTranscriptRepository>()                │
│                                                                       │
│ Migrations/<timestamp>_AddMeetingTasksTables.cs                      │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                  PostgreSQL: public.MeetingTranscripts
                              public.ProposedTasks
```

Downstream consumers (Plaud ingestion handler, review UI MediatR handlers) will live in `Anela.Heblo.Application/Features/MeetingTasks/` in later subtasks and consume `IMeetingTranscriptRepository` only.

### Key Design Decisions

#### Decision 1: Auto-register configurations vs. explicit registration

**Options considered:** (a) call `modelBuilder.ApplyConfiguration(new MeetingTranscriptConfiguration())` explicitly in `OnModelCreating`; (b) rely on the existing `ApplyConfigurationsFromAssembly` call.

**Chosen approach:** (b) — do nothing in `OnModelCreating`. Just create the configuration classes in the `Anela.Heblo.Persistence` assembly.

**Rationale:** The line `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` is already present (`ApplicationDbContext.cs:127`) and is how every existing module's configurations get picked up. Adding explicit registration would be redundant and inconsistent. FR-6's wording ("auto-applied via existing `ApplyConfigurationsFromAssembly` (if present) or explicit registration") resolves cleanly: the assembly scan is present, so no extra line is needed.

#### Decision 2: Repository registration location

**Options considered:** (a) register `IMeetingTranscriptRepository → MeetingTranscriptRepository` in a new `MeetingTasksModule.AddMeetingTasksModule()` in the Application layer; (b) register it directly in `PersistenceModule.AddPersistenceServices()` alongside the other repositories.

**Chosen approach:** (b) — register in `PersistenceModule.AddPersistenceServices()`.

**Rationale:** Every other repository in the project (`IKnowledgeBaseRepository`, `IClassificationRuleRepository`, `IGridLayoutRepository`, etc.) is registered in `PersistenceModule`, not in an Application-layer module. Doing the same keeps the spec self-contained (no need to scaffold an empty `MeetingTasksModule` in `Anela.Heblo.Application` just for one line of DI), and removes the "deferred unless required to compile" ambiguity in the spec. The Application-layer `MeetingTasksModule.cs`/`MeetingTasksOptions.cs` can be introduced by the next subtask when there is an actual handler/service to wire up.

#### Decision 3: Rely on global DateTime converter instead of property-level UTC enforcement

**Options considered:** (a) add a UTC `ValueConverter` to each timestamp property; (b) rely on the existing global UTC value converter in `ApplicationDbContext.OnModelCreating` plus `AsUtcTimestamp()` for column type.

**Chosen approach:** (b) — match the spec verbatim. `AsUtcTimestamp()` sets `HasColumnType("timestamp")`; the DbContext's global converter handles `DateTimeKind` translation for all DateTime properties.

**Rationale:** The global converter in `ApplicationDbContext.cs:130–153` already applies to every entity's `DateTime`/`DateTime?` property. The spec's column-level `AsUtcTimestamp()` only sets the column type — which is exactly what every other module does. No duplication, no surprise behavior. This matches the project's existing convention documented in `docs/architecture/Dev_Guidelines_time.md`/`DateTime_StandardizationGuide.md`.

#### Decision 4: Status enum stored as string via default `HasConversion<string>()`

**Options considered:** (a) store as int; (b) store as PascalCase string via default `HasConversion<string>()`; (c) store as lowercase string via custom converter (as `KnowledgeBaseDocumentConfiguration` does).

**Chosen approach:** (b) — `HasConversion<string>()` with default `Enum.ToString()` (PascalCase).

**Rationale:** Spec asks for this explicitly (FR-5: "stored as string via `HasConversion<string>()`"). Resilient to enum reordering, readable in DB, sufficient for the bounded set of three statuses each. KnowledgeBase chose lowercase for legacy/external reasons that don't apply here. Keeping default PascalCase reduces conversion surface area.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/MeetingTasks/
├── MeetingTranscript.cs
├── ProposedTask.cs
├── MeetingTranscriptStatus.cs
├── ProposedTaskStatus.cs
└── IMeetingTranscriptRepository.cs

backend/src/Anela.Heblo.Persistence/MeetingTasks/
├── MeetingTranscriptConfiguration.cs
├── ProposedTaskConfiguration.cs
└── MeetingTranscriptRepository.cs

backend/src/Anela.Heblo.Persistence/
├── ApplicationDbContext.cs        (add 2 DbSets + using)
├── PersistenceModule.cs           (add 1 AddScoped registration)
└── Migrations/
    ├── <timestamp>_AddMeetingTasksTables.cs
    └── <timestamp>_AddMeetingTasksTables.Designer.cs
```

**Do not create** in this subtask:
- `Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- `Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`
- DTOs

These are deferred to the first consumer subtask. The brief's file-structure list includes them, but the spec correctly notes they are out of scope.

### Interfaces and Contracts

The interface defined in the spec is the only public contract introduced. Bind it through DI as scoped (matches every other repository's lifetime in `PersistenceModule`):

```csharp
// In PersistenceModule.AddPersistenceServices, alongside the other "// X repositories" sections:
// Meeting Tasks repositories
services.AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>();
```

No public DTO, controller, or HTTP contract is introduced.

### Data Flow

For this subtask only the write/read paths through the repository exist; no business handlers yet:

```
Future Plaud ingestion handler
  → IMeetingTranscriptRepository.ExistsByPlaudIdAsync(plaudId)   // dedup gate
  → new MeetingTranscript { ..., Tasks = [...] }
  → IMeetingTranscriptRepository.AddAsync(transcript)
  → IMeetingTranscriptRepository.SaveChangesAsync()
        → EF Core inserts row in MeetingTranscripts +
          cascaded rows in ProposedTasks (single transaction)

Future review handler
  → IMeetingTranscriptRepository.GetByIdAsync(id)
        → SELECT ... FROM MeetingTranscripts WHERE Id = @id
        → LEFT JOIN ProposedTasks (via Include)
  → mutate Status/ReviewedAt/ReviewedByUser on aggregate
  → IMeetingTranscriptRepository.SaveChangesAsync()

Future list endpoint
  → IMeetingTranscriptRepository.GetListAsync(statusFilter, page, pageSize)
        → filtered + Include(Tasks) + OrderByDescending(PlaudCreatedAt) + paginate
```

DB-level: `UX_MeetingTranscripts_PlaudRecordingId` provides idempotency under concurrent insert (a second writer with the same `PlaudRecordingId` gets a unique-constraint violation rather than a silent duplicate — desirable defense-in-depth even with the app-level `ExistsByPlaudIdAsync` check).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Repository registration omitted from `PersistenceModule` — code compiles, DI fails at request time when first consumer arrives. | HIGH | Make the registration step explicit in the task list (see Specification Amendments). Add the `AddScoped` line in the same commit as the repository implementation. |
| `RawTranscript` can be very large; deserializing on every `GetByIdAsync`/`GetListAsync` (with `Include(Tasks)`) loads it even when only metadata is needed. | MEDIUM | Acceptable for this subtask (only listing/details endpoints will use it). When list UI is built, add a `GetListSummaryAsync` projection that excludes `RawTranscript`. Out of scope here. |
| Postgres `TEXT` storage for transcript means full row pulled for every query without projection. | LOW | EF Core's default Include behavior is fine for current expected volumes. Revisit if/when transcript size or row count grows materially. |
| Race on dedup: app-level `ExistsByPlaudIdAsync` could pass and then a concurrent ingest commits first. | LOW | Already mitigated by `UX_MeetingTranscripts_PlaudRecordingId` unique index — caller will get a `DbUpdateException` with Postgres error 23505. Ingestion handler subtask should catch and convert to an idempotent "already processed" response. Worth noting in that subtask's spec. |
| Designer files for migrations are committed and can churn with unrelated changes. | LOW | Standard EF Core flow; matches project history. No action needed. |
| Status string drift if someone manually edits DB rows in mixed casing. | LOW | Acceptable — default `HasConversion<string>()` is case-sensitive on read. If this ever bites, switch to the case-insensitive form used in `KnowledgeBaseDocumentConfiguration`. Not worth pre-emptively diverging. |
| Migration generated against a worktree with a different `ApplicationDbContext` than `main` may produce conflicts on the long migration timeline. | LOW | Per project facts, migrations are manual; the team rebases/regenerates as needed. Branching from `feat/meeting-task-validation-epic` (per spec) keeps the migration on a stable base. |

## Specification Amendments

1. **Add an explicit DI registration step (closes the open question in the spec's API/Interface Design section).**
   In `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`, inside `AddPersistenceServices`, add:
   ```csharp
   // Meeting Tasks repositories
   services.AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>();
   ```
   Reason: the codebase does **not** use convention-based DI registration; every repository is registered explicitly there. Without this line the feature compiles but cannot resolve the dependency at runtime. Belongs in Task 2 alongside the repository implementation.

2. **Remove the "if present" hedge in FR-6.** `ApplicationDbContext.OnModelCreating` already calls `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` (verified at line 127). FR-6 should state plainly: configurations in the `Anela.Heblo.Persistence` assembly are auto-applied — no explicit registration in `OnModelCreating` is required or permitted (would duplicate the scan).

3. **Confirm `MeetingTasksModule.cs` / `MeetingTasksOptions.cs` / DTOs are out of scope in this PR.** The spec's "Out of Scope" already says this, but the brief's file structure lists them, so an explicit note in the task list prevents accidental scope creep. Treat them as belonging to the next subtask (the first consumer).

4. **Migration command working directory.** Spec says to run `dotnet ef migrations add` from `backend/src/Anela.Heblo.API/`. Verify `DesignTimeDbContextFactory.cs` is present in the Persistence project (it is — confirmed in worktree). Add `--startup-project ../Anela.Heblo.API/` only if running from the Persistence project directory; from API the spec's command is correct.

5. **Note on `Status` enum string casing.** The spec leaves the conversion to default `HasConversion<string>()` (PascalCase). Document this explicitly so a future migration to lowercase doesn't silently break enum parsing. No code change — just clarity for future maintainers.

## Prerequisites

All prerequisites are already present in the worktree — no new infrastructure or config needed:

- `Anela.Heblo.Persistence/Extensions/DateTimeConfigurationExtensions.cs` — exists, provides `AsUtcTimestamp()`.
- `Anela.Heblo.Persistence/ApplicationDbContext.cs` — exists, has assembly-scan for configurations and a global UTC `DateTime` value converter.
- `Anela.Heblo.Persistence/DesignTimeDbContextFactory.cs` — exists, enables `dotnet ef migrations add`.
- `Anela.Heblo.Persistence/PersistenceModule.cs` — exists, is the single registration point for all repositories.
- `Microsoft.EntityFrameworkCore` / `.Design` / `Npgsql.EntityFrameworkCore.PostgreSQL` — already referenced.
- Branch base: `feat/meeting-task-validation-epic` — the subtask branches from and PRs back into this branch, not `main` (per spec Dependencies section).
- Postgres `public` schema is the project standard (confirmed by migration `MoveTablesFromDboToPublicSchema`).

No new packages, no new schema, no new infrastructure. Implementation can start immediately once the spec amendments above are accepted.