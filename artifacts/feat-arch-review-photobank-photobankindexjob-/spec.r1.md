# Specification: Refactor PhotobankIndexJob to use IPhotobankRepository

## Summary
`PhotobankIndexJob` is an Application-layer class that violates Clean Architecture by directly depending on the Persistence layer's `ApplicationDbContext` and EF Core APIs. This work extends `IPhotobankRepository` with the operations the job needs, replaces all `_db.*` access with repository calls, and updates the existing tests to exercise the job through the repository abstraction — bringing the job into alignment with its sibling `PhotobankAutoTagJob`.

## Background
The Photobank module defines `IPhotobankRepository` (Domain layer) as the boundary for all data access from the Application layer. `PhotobankRepository` (Application layer) implements that interface against `ApplicationDbContext`. `PhotobankAutoTagJob` already follows this pattern correctly.

`PhotobankIndexJob` bypasses the abstraction entirely:

- Injects `ApplicationDbContext _db` (line 13, 28, 33)
- Reads roots via `_db.PhotobankIndexRoots.Where(...).ToListAsync` (lines 46–48)
- Reads tag rules via `_db.PhotobankTagRules.Where(...).ToListAsync` (lines 77–80)
- Reads, adds, and removes photos via `_db.Photos.*` (lines 88, 91, 120, 132)
- Reads and removes `PhotoTag` rows via `_db.PhotoTags.*` (lines 148–151, 159)
- Reads and adds `Tag` rows via `_db.PhotobankTags.*` (lines 156, 174)
- Calls `_db.SaveChangesAsync(ct)` directly (lines 72, 104, 145, 168, 175)

Consequences:

- The job's only test (`backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs`) cannot mock the repository — it uses an EF Core `InMemoryDatabase`, which has known divergences from the real PostgreSQL behavior and forces the test to know about persistence wiring.
- Any future schema change or ORM replacement requires editing job logic, not just the repository.
- The project's own boundary rule (encoded in `PhotobankModule.cs` line 31 registering `IPhotobankRepository`) is broken.

## Functional Requirements

### FR-1: Extend `IPhotobankRepository` with index-job operations
Add the missing read/write operations needed by `PhotobankIndexJob` to the interface in `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`. The new operations must use Domain types only (no EF Core or `ApplicationDbContext` types in signatures).

The following new operations are required:

1. `Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken)` — returns active roots that have a non-null `DriveId` (replaces lines 46–48). The roots must remain tracked so subsequent `RootItemId`/`DeltaLink`/`LastIndexedAt` mutations are persisted on `SaveChangesAsync`.
2. `Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken)` — returns active tag rules ordered by `SortOrder` (replaces lines 77–80).
3. `Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken)` — finds a `Photo` by `SharePointFileId` (replaces lines 88 and 120). Must return a tracked entity so subsequent mutations are persisted.
4. `Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken)` — adds a new `Photo` (replaces line 132).
5. `Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken)` — removes a `Photo` (replaces line 91).
6. `Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken)` — returns existing photo tags for a photo filtered by source (replaces lines 148–150).
7. `Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken)` — removes the given photo tags (replaces line 151).

Reuse of existing repository methods:

- `GetTagByNameAsync(string normalizedName, CancellationToken)` — replaces line 156's tag lookup.
- `AddPhotoTagAsync(PhotoTag, CancellationToken)` — replaces line 159's add.
- `SaveChangesAsync(CancellationToken)` — replaces every `_db.SaveChangesAsync(ct)` call.

For tag creation (line 156 `?? await CreateTagAsync(...)`), use the existing `GetOrCreateTagAsync(string normalizedName, CancellationToken)`. This consolidates the lookup-or-create pattern that `PhotobankIndexJob`'s private `CreateTagAsync` helper currently duplicates.

**Acceptance criteria:**
- New methods compile and are present on both `IPhotobankRepository` and `PhotobankRepository`.
- `IPhotobankRepository` continues to depend only on the Domain layer (no `Microsoft.EntityFrameworkCore` references).
- No new abstractions beyond what the job calls are added (no "future-proofing" methods).

### FR-2: Refactor `PhotobankIndexJob` to use the repository
Modify `backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`:

- Replace the `private readonly ApplicationDbContext _db;` field with `private readonly IPhotobankRepository _repo;`.
- Replace the constructor's `ApplicationDbContext db` parameter with `IPhotobankRepository repo`.
- Remove the `using Anela.Heblo.Persistence;` and `using Microsoft.EntityFrameworkCore;` directives. The file must not reference any EF Core or Persistence types after the change.
- Replace every `_db.*` data access with the corresponding repository method per FR-1.
- Remove the private `CreateTagAsync` helper (lines 171–177); use `_repo.GetOrCreateTagAsync` instead.
- Behavior must be identical to the current implementation, including: ordering of `SaveChangesAsync` calls, the `pathChanged → photo.LastAutoTaggedAt = null` invalidation, the deletion path for items where `IsDeleted == true`, the rule-tag re-apply sequence (remove all `Rule`-source tags first, then add matched ones), and the per-root try/catch that logs and continues.

**Acceptance criteria:**
- A search for `ApplicationDbContext` or `Microsoft.EntityFrameworkCore` in the job file returns no matches.
- The job's public API (constructor parameter set apart from the `_db → _repo` swap, `Metadata`, `ExecuteAsync`) is unchanged.
- The number of `SaveChangesAsync` calls and their relative position in the flow match the current implementation exactly.

### FR-3: Verify DI registration
`IPhotobankRepository` is already registered in `PhotobankModule.cs` line 31. `PhotobankIndexJob` registration (if it relies on convention-based scanning today) must continue to work; if it was implicitly resolvable only via `ApplicationDbContext` injection, add an explicit `services.AddScoped<PhotobankIndexJob>()` mirroring line 32 for `PhotobankAutoTagJob`. Verify by reviewing the current file; do not add a duplicate registration.

**Acceptance criteria:**
- `PhotobankIndexJob` resolves successfully from the DI container at application startup.
- No additional registrations beyond what's needed for the refactor.

### FR-4: Update `PhotobankIndexJobTests` to use the repository
Rewrite `backend/test/Anela.Heblo.Tests/Features/Photobank/PhotobankIndexJobTests.cs` so the job is constructed against `Mock<IPhotobankRepository>` instead of a real `ApplicationDbContext`.

- Remove the `using Anela.Heblo.Persistence;`, `using Microsoft.EntityFrameworkCore;`, and `IDisposable` machinery used to manage the in-memory `DbContext`.
- Replace the `_db` field with `Mock<IPhotobankRepository> _repoMock`.
- Each existing test case must continue to assert the same observable behavior (e.g. "inserts new photo with rule tags applied", "removes photo on delete", "updates `DeltaLink`/`LastIndexedAt`", "resets `LastAutoTaggedAt` on path change"). Adjust assertions from "inspect rows in `_db`" to "verify repository method was called with the expected entity/arguments" using Moq's `Verify` and argument captors, or by tracking state in a fake test double if Verify-only assertions become unreadable.

**Acceptance criteria:**
- All test cases that existed before the refactor still exist (same names, same scenarios). No tests deleted.
- New tests are NOT added in this work; only the existing tests are migrated.
- `dotnet test --filter FullyQualifiedName~PhotobankIndexJobTests` passes.
- The test file contains no `ApplicationDbContext` or `Microsoft.EntityFrameworkCore` references.

### FR-5: Behavior preservation — no functional change
This refactor must not change any observable behavior of the indexing job. In particular:

- The cron expression, job name, display name, description, and default-enabled flag in `Metadata` are unchanged.
- The disabled-job short-circuit (lines 40–44) is unchanged.
- The per-root try/catch (lines 66–115) continues to swallow exceptions per root, log them with `RootId`, and let the loop continue to the next root.
- The order of writes within `IndexRootAsync` and `UpsertPhotoAsync` is preserved so transactional semantics match: `RootItemId` resolution flushes immediately; `UpsertPhotoAsync` flushes after photo upsert and again after rule-tag reapply; root delta-link update flushes at the end of the root loop.

**Acceptance criteria:**
- No EF Core migrations are added (no schema change).
- A manual code review confirms the sequence of `SaveChangesAsync` calls in the refactored job matches the original.

## Non-Functional Requirements

### NFR-1: Performance
No measurable regression in indexing throughput. The current implementation issues one query per photo for the existing-photo lookup and one query per matched tag name for the tag lookup; the refactored implementation must keep the same query shape (a per-photo `FirstOrDefaultAsync` on `SharePointFileId`, a per-tag-name `FirstOrDefaultAsync` on `Name`). Do NOT introduce batching, caching, or other optimizations in this work — those are out of scope.

### NFR-2: Security
No security surface change. The job runs as a recurring background job, holds no user-facing endpoints, and the refactor does not alter what data is read or written.

### NFR-3: Test coverage
The migrated `PhotobankIndexJobTests` suite must achieve the same scenario coverage as before (counted by test method count). Lines covered may change due to the test approach, but no scenario should be dropped.

### NFR-4: Clean Architecture compliance
After the change, `PhotobankIndexJob.cs` must have no `using` directive on `Anela.Heblo.Persistence` or `Microsoft.EntityFrameworkCore`, mirroring `PhotobankAutoTagJob.cs`. This is verified by a simple grep in the acceptance check.

## Data Model
No data-model changes. Entities involved (read by the job): `PhotobankIndexRoot`, `TagRule`, `Photo`, `PhotoTag`, `Tag`. All entity definitions in `backend/src/Anela.Heblo.Domain/Features/Photobank/` remain untouched.

The repository interface gains methods returning these existing Domain types; no new DTOs or projections are introduced.

## API / Interface Design
No public/HTTP API changes. The only interface change is internal: `IPhotobankRepository` gains the methods listed in FR-1.

Signatures (final shape):

```csharp
Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken);
Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken);
Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken);
Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken);
Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken);
Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
```

The Add/Remove methods may be synchronous internally (just `_context.Set.Add/Remove`) but should return `Task` to match the established interface convention seen in `AddPhotoTagAsync`, `AddRuleAsync`, etc.

The mutated `PhotobankIndexRoot` instance returned by `GetActiveRootsWithDriveAsync` must be EF-tracked so the in-job mutations (`root.RootItemId`, `root.DeltaLink`, `root.LastIndexedAt`) are persisted by the subsequent `SaveChangesAsync()` call. This matches how `PhotobankRepository.GetRootsAsync` already behaves (no `AsNoTracking`).

## Dependencies
- `IPhotobankRepository` (Domain) — extended.
- `PhotobankRepository` (Application) — implements new methods.
- `PhotobankIndexJob` (Application) — refactored consumer.
- `PhotobankIndexJobTests` (test project) — migrated to mocks.
- No external services, no new NuGet packages.

## Out of Scope
- Refactoring or extending `PhotobankAutoTagJob` (already compliant).
- Refactoring other consumers of `ApplicationDbContext` in the Application layer (separate arch-review findings).
- Adding batching/eager-loading/caching to reduce per-photo queries in indexing.
- Replacing the existing `CreateTagAsync` lookup-then-insert pattern with a transactional upsert (the existing `GetOrCreateTagAsync` may have the same race-condition characteristics; preserving current behavior is explicit).
- Adding new test scenarios beyond migrating what exists.
- Adding integration tests against a real PostgreSQL instance.
- Changing the cron schedule, job metadata, or any operational configuration.
- Removing the EF Core in-memory provider from the test project (other tests may still use it).

## Open Questions
None.

## Status: COMPLETE