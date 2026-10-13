# Architecture Review: Remove redundant `JobName` column from `RecurringJobConfiguration`

## Skip Design: true
Backend-only persistence/repository cleanup with a schema migration. No new or changed UI components, screens, layouts, or visual design decisions are involved. `RecurringJobDto.JobName` (the field exposed to the frontend via `BackgroundJobsMappingProfile`) keeps returning the same value, so the frontend is untouched. The designer phase can be skipped.

## Architectural Fit Assessment
This fits cleanly into the existing Vertical Slice / Clean Architecture layering already used for `BackgroundJobs`:
- Domain entity: `Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
- Persistence/EF config: `Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
- Repository: `Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` (implements `IRecurringJobConfigurationRepository` from Domain)
- Consumers: `RecurringJobSeeder` (Application layer) and `BackgroundJobsMappingProfile` (AutoMapper → `RecurringJobDto`)

The change is fully contained inside this existing module boundary — no cross-module coupling, no new dependency, no public API/contract change (`RecurringJobDto` shape is unchanged). This is a textbook "collapse a redundant column" cleanup, and the codebase already has a precedent for narrow, single-purpose migrations against this exact table (`20260718074122_AddTimeZoneIdToRecurringJobConfigurations`), so the pattern to follow is well established.

The only architecturally interesting point is a deployment-behavior fact from `docs/architecture/infrastructure.md` §5 that the brief and CLAUDE.md's blanket "migrations are manual" statement don't surface: **migrations apply automatically on Production startup** (Development/Test/Staging remain manual). This is called out explicitly in Risks below because it changes how conservative the migration must be.

## Proposed Architecture

### Component Overview
```
RecurringJobSeeder (Application)
        │  config.JobName (in-memory read, AutoMapper-style convenience)
        ▼
RecurringJobConfiguration (Domain entity)
   Id            : string  (PK, EF-mapped)         ──┐
   JobName       : string  (derived, NOT EF-mapped)  │ always equal
        │                                             │  by invariant
        ▼                                            ─┘
RecurringJobConfigurationConfiguration (EF config, Persistence)
   builder.HasKey(e => e.Id)
   builder.Property(e => e.Id) ...
   [JobName property mapping REMOVED]
   [IX_RecurringJobConfigurations_JobName index REMOVED]
        │
        ▼
RecurringJobConfigurationRepository (Persistence)
   GetByJobNameAsync(jobName) → filters on c.Id == jobName   (was c.JobName)
   GetAllAsync()              → orders by c.Id               (was c.JobName)
        │
        ▼
RecurringJobConfigurations table (Postgres, schema "public")
   Id column only for the identifier (JobName column + its unique index dropped
   via a new EF Core migration)
```

No new components are introduced. The entity, its EF configuration, its repository, and one new migration file are the only pieces touched.

### Key Design Decisions

#### Decision 1: Keep `JobName` as a computed read-only property, don't remove it from the C# API
**Options considered:**
1. Delete `JobName` entirely and require every caller to use `Id`.
2. Keep `JobName` as an independent settable field but stop EF from mapping it (`[NotMapped]`), leaving the constructor's `JobName = jobName;` assignment as-is.
3. Turn `JobName` into a computed, read-only property (`public string JobName => Id;`), removing the private backing field and its constructor assignment entirely.

**Chosen approach:** Option 3 — `public string JobName => Id;`, with the constructor's `JobName = jobName;` line deleted (only `Id = jobName;` remains) and the `private set` removed since there is nothing left to set.

**Rationale:** Option 1 breaks `RecurringJobSeeder.cs` (`config.JobName`) and `RecurringJobDto` AutoMapper convention-mapping for no benefit — the spec explicitly keeps the public API stable (NFR-2). Option 2 keeps two independently-assignable fields that can still drift apart in memory even though the drift no longer reaches the database — it does not actually close the DRY gap the brief flags, it only removes the redundant column. Option 3 makes `Id == JobName` a compiler-enforced invariant instead of a constructor convention comment (`// JobName is the primary key`), which is strictly better and costs nothing since nothing in the codebase ever needs to set `JobName` independently of `Id` (confirmed by grep: the only writer is the constructor, and only with the same value as `Id`).

`[NotMapped]` is unnecessary with this approach: a property with no setter and an expression body (`=>`) is never picked up by EF Core's convention-based mapping in the first place, so there is nothing to annotate. This is simpler than the brief's suggested `[NotMapped]` attribute and achieves the same persistence outcome.

#### Decision 2: One additive-safe EF Core migration that drops the column and index
**Options considered:**
1. Hand-write raw SQL migration.
2. Use `dotnet ef migrations add` to autogenerate from the updated model, following the exact pattern of the existing `AddTimeZoneIdToRecurringJobConfigurations` migration in the same directory.

**Chosen approach:** Option 2.

**Rationale:** Every other migration in `Anela.Heblo.Persistence/Migrations` is EF-tool-generated; hand-writing SQL would fight the model snapshot (`ApplicationDbContextModelSnapshot.cs`) and require manually keeping it in sync, which is exactly the kind of DRY drift this issue is about avoiding. Running `dotnet ef migrations add DropRedundantJobNameColumn --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` after the entity/config changes lets EF Core diff the model and generate `Up()`/`Down()` (dropping the index before/after the column as EF orders it) and update the snapshot in one consistent step.

#### Decision 3: `Down()` must restore both the column and the unique index, not just the column
**Options considered:**
1. Accept whatever `dotnet ef migrations add` generates without inspection.
2. Explicitly verify the generated `Down()` recreates `IX_RecurringJobConfigurations_JobName` as a **unique** index with the same name, not just the column.

**Chosen approach:** Option 2 — verify, and hand-fix if the tool's diff omits the uniqueness or the explicit `HasDatabaseName`.

**Rationale:** EF's migration generator is generally reliable for this exact shape (property removal + index removal), but this is a one-way-feeling schema change (dropping a PK-adjacent unique constraint) where a silently wrong `Down()` would only be discovered during an actual rollback, i.e. the worst possible time. A 30-second manual read of the generated migration file is cheap insurance; regenerating with wrong `Down()` and only noticing at rollback time is not.

## Implementation Guidance

### Directory / Module Structure
No new files or directories beyond one new migration pair. Exact files touched:
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — collapse `JobName` into a computed property, remove its constructor assignment and backing `private set`.
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — remove the `builder.Property(e => e.JobName)...` block and the `builder.HasIndex(e => e.JobName)...` block; keep the `IsEnabled` index and all other configuration untouched.
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — `GetByJobNameAsync` filters on `c.Id`; `GetAllAsync` orders by `c.Id`.
- `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs` + `.Designer.cs` (new, tool-generated) — drops the `JobName` column and `IX_RecurringJobConfigurations_JobName` index.
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — updated automatically by `dotnet ef migrations add`; do not hand-edit.

No changes needed to `IRecurringJobConfigurationRepository.cs` (interface signatures are unchanged), `RecurringJobSeeder.cs`, `BackgroundJobsMappingProfile.cs`, or `RecurringJobDto.cs` — all read `JobName` in-memory off an already-materialized entity, which keeps working identically against the computed property.

### Interfaces and Contracts
No public interface, DTO, or OpenAPI contract changes. `IRecurringJobConfigurationRepository.GetByJobNameAsync(string jobName)` keeps its exact signature and semantics (parameter name `jobName` stays — it's the argument value, not a column reference). `RecurringJobDto.JobName` keeps its exact shape; the generated TypeScript client is unaffected since no C# DTO changed.

### Data Flow
1. **Seeding** (`RecurringJobSeeder.SeedDefaultConfigurationsAsync`, on app startup for discovered `IRecurringJob` implementations): constructs `RecurringJobConfiguration` with `jobName`, reads back `config.JobName` (now `== config.Id`, in-memory, no DB round-trip) to call `GetByJobNameAsync`, which now issues `WHERE "Id" = @jobName` against Postgres instead of `WHERE "JobName" = @jobName`. Same rows returned, since `Id` and the old `JobName` column always held identical values.
2. **Listing** (`GetAllAsync`, used by whatever UI/controller lists jobs via `RecurringJobDto`): now issues `ORDER BY "Id"` instead of `ORDER BY "JobName"` — identical resulting order, since the two columns' values were always identical.
3. **DTO projection**: `RecurringJobConfiguration` → `RecurringJobDto` via AutoMapper's convention mapping happens entirely in memory after the entity is loaded, so it reads the computed `JobName` property exactly as before; no behavior change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Migration auto-applies on Production startup (per `docs/architecture/infrastructure.md` §5) with no manual gate, unlike Dev/Test/Staging | Medium | Keep the migration a pure column+index drop with a correct, verified `Down()`; no data transformation logic that could fail mid-deploy. Call this out explicitly in the PR description per CLAUDE.md's migration guidance so it isn't missed during review. |
| Generated `Down()` migration doesn't correctly restore the unique constraint (only the column) | Low-Medium | Manually inspect the generated migration file per Decision 3 before committing; fix by hand if `HasDatabaseName`/`IsUnique` is missing from the regenerated `Up()`/`Down()` pair. |
| A query elsewhere in the codebase filters/orders on `JobName` against the DbContext and was missed | Low | Confirmed via repo-wide grep (this review + the spec's FR-3) that `RecurringJobConfigurationRepository` is the only place with `DbSet<RecurringJobConfiguration>` LINQ queries referencing `JobName`; both call sites (`GetByJobNameAsync`, `GetAllAsync`) are enumerated in the spec. If a query is missed, EF Core throws an `InvalidOperationException` at query-translation time (fails loudly, not silently), so this is a build/smoke-test-catchable class of bug, not a silent-corruption one. |
| Local/staging DBs need the migration applied manually and could drift from the entity model if forgotten | Low | Standard, pre-existing process risk (per CLAUDE.md, all migrations are manual outside Production) — no new mitigation needed beyond the existing `dotnet ef database update` step already required for every migration. |

## Specification Amendments
- **FR-1 implementation detail:** the spec allows either `[NotMapped]` or a computed property; this review resolves that choice to a computed, read-only property (`public string JobName => Id;`) with no `[NotMapped]` attribute needed — see Decision 1. The planner should scope the task accordingly (remove the backing field/setter, not just add an attribute).
- **FR-2 clarification:** the new migration should be generated via `dotnet ef migrations add`, not hand-written, and its `Down()` must be manually verified to restore both the column and the named unique index (see Decision 3) before it's committed.
- **FR-3 clarification:** two call sites need updating in `RecurringJobConfigurationRepository.cs`, not one — `GetByJobNameAsync` (`FirstOrDefaultAsync` filter) and `GetAllAsync` (`OrderBy`). Both are already listed in spec.r1.md's FR-3 (post-review revision); no further spec text change needed beyond what's already there.
- No other amendments — the spec's Data Model, API/Interface Design, and Out of Scope sections are architecturally sound as written.

## Prerequisites
- None beyond a working local EF Core tooling setup (`dotnet ef`) to generate the migration — no new infrastructure, config, or external dependency is required.
- The developer must run `dotnet build` after the entity/config changes and before generating the migration, so `dotnet ef migrations add` diffs against a compiling model.
