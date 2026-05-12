Tests verified (6/6 pass). The PR is already open at https://github.com/onpaj/Anela.Heblo/pull/1145 targeting `feat/meeting-task-validation-epic`. This is a full git clone under `.worktrees/` (not a `git worktree add` worktree).

Implementation complete. What would you like to do?

1. Merge back to `feat/meeting-task-validation-epic` locally
2. Push and create a Pull Request *(already done — PR #1145 is open)*
3. Keep the branch as-is (I'll handle it later)
4. Discard this work

Which option?

---

Here's a summary of what was implemented while you decide:

# Implementation: Meeting Tasks — Domain Model & EF Core Persistence

## What was implemented

Foundation data layer for the Meeting Task Validation Checkpoint epic. Adds `MeetingTranscript` aggregate with `ProposedTask` children, persisted via EF Core to PostgreSQL through a clean repository interface.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscriptStatus.cs` — aggregate status enum
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTaskStatus.cs` — child task status enum
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/ProposedTask.cs` — child entity class
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` — aggregate root class
- `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/IMeetingTranscriptRepository.cs` — repository interface (5 methods)
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` — EF config: table, indexes, cascade delete
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/ProposedTaskConfiguration.cs` — EF config: child table, FK index
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` — EF implementation (Include, pagination, AnyAsync)
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — added 2 DbSets + using
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — added `AddScoped<IMeetingTranscriptRepository, MeetingTranscriptRepository>()`
- `backend/src/Anela.Heblo.Persistence/Migrations/20260512191541_AddMeetingTasksTables.cs` — migration creating both tables

## Tests

`backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTranscriptRepositoryTests.cs` — 6 unit tests using `UseInMemoryDatabase`, covering GetById, GetList with filtering/pagination/ordering, ExistsByPlaudId, and AddAsync with child persistence

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln          # 0 errors
dotnet format backend/Anela.Heblo.sln --verify-no-changes   # 0 diffs
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MeetingTranscriptRepositoryTests"  # 6 pass
```

## Notes

- `AsNoTracking()` not added to list/read queries; noted for the consumer PR when read vs. write intent per endpoint is clear
- No API, DTOs, or Application-layer handlers in this PR (deferred to next subtask)
- Migration must be applied manually against Postgres dev instance per project facts

## PR Summary

Adds the `MeetingTranscript` aggregate and `ProposedTask` child entity to the domain, configures them for EF Core persistence, and generates the `AddMeetingTasksTables` migration — the prerequisite data layer for the full Meeting Task Validation Checkpoint epic.

### Changes
- `Domain/Features/MeetingTasks/` — 5 new files: 2 enums, 2 entity classes, 1 repository interface
- `Persistence/MeetingTasks/` — 3 new files: 2 EF configs, 1 repository implementation
- `ApplicationDbContext.cs` — 2 DbSets added
- `PersistenceModule.cs` — 1 DI registration added
- `Migrations/20260512191541_AddMeetingTasksTables.cs` — creates `public.MeetingTranscripts` and `public.ProposedTasks` with 4 indexes and cascade FK

## Status
DONE