# Implementation: create-smartsupp-webhook-audit-repository

## What was implemented

Introduced `ISmartsuppWebhookAuditRepository` (Domain) / `SmartsuppWebhookAuditRepository`
(Persistence) covering create, update-outcome, list, get, get-for-replay, save, and purge for
`SmartsuppWebhookAuditEntry`, following the existing `ISmartsuppPresenceRepository` /
`SmartsuppPresenceRepository` precedent (ADR-004). Rewired `ListWebhookAuditHandler`,
`GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`, `SmartsuppWebhookAuditCleanupJob`, and
`SmartsuppWebhookController` to depend on the new repository instead of
`Anela.Heblo.Persistence.ApplicationDbContext` / the old `ISmartsuppWebhookAuditWriter`. Deleted
`ISmartsuppWebhookAuditWriter` and `SmartsuppWebhookAuditWriter` entirely. Updated the DI binding in
`SmartsuppModule.cs`. Migrated the four existing handler/job unit test files to wrap the in-memory
`ApplicationDbContext` in a real `SmartsuppWebhookAuditRepository` instead of passing the context
directly to the unit under test, and renamed/extended `SmartsuppWebhookAuditWriterTests.cs` into
`SmartsuppWebhookAuditRepositoryTests.cs` with the new `ListAsync`/`GetByIdAsync`/
`GetForReplayAsync`/`PurgeOlderThanAsync` test cases appended. No behavior, HTTP contract, or schema
change — `ListAsync` now returns domain entities (with `.AsNoTracking()` explicitly, replacing the
implicit no-tracking semantics of the old `.Select(...)` DTO projection) and DTO projection was moved
into `ListWebhookAuditHandler`, per the approved Specification Amendment #2 in the task context.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppWebhookAuditRepository.cs` — new
  repository contract (CreateAsync, UpdateOutcomeAsync, ListAsync, GetByIdAsync, GetForReplayAsync,
  SaveChangesAsync, PurgeOlderThanAsync).
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditRepository.cs` — new EF Core
  implementation of the above, wrapping `ApplicationDbContext`.
- `backend/src/Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs` — deleted.
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditWriter.cs` — deleted.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` — DI binding swapped
  from `ISmartsuppWebhookAuditWriter`/`SmartsuppWebhookAuditWriter` to
  `ISmartsuppWebhookAuditRepository`/`SmartsuppWebhookAuditRepository`.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs`
  — now injects the repository and maps `ListAsync` results to `WebhookAuditSummaryDto` in the
  handler instead of the old EF Core inline query/projection.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs`
  — now uses `GetByIdAsync`.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`
  — now uses `GetForReplayAsync` + `SaveChangesAsync`.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs`
  — now uses `PurgeOlderThanAsync(cutoff, ct)`, keeping the `RetentionDays = 7` /
  `DateTime.UtcNow.AddDays(-RetentionDays)` computation in the job.
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs` — injects
  `ISmartsuppWebhookAuditRepository` instead of `ISmartsuppWebhookAuditWriter`; dropped the
  `using Anela.Heblo.Persistence.Smartsupp;` import; the four `CreateAsync`/`UpdateOutcomeAsync`
  call sites are unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ListWebhookAuditHandlerTests.cs`,
  `GetWebhookAuditEntryHandlerTests.cs`, `ReplayWebhookEventHandlerTests.cs`,
  `SmartsuppWebhookAuditCleanupJobTests.cs` — constructor calls for the unit under test now wrap
  `ctx` in `new SmartsuppWebhookAuditRepository(ctx)`; all other arrange/assert code unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditWriterTests.cs`
  — deleted (renamed).
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditRepositoryTests.cs`
  — new (renamed + extended): the two original writer tests plus 7 new tests covering `ListAsync`
  (ordering, filtering, paging), `GetByIdAsync` (found/not-found), `GetForReplayAsync` +
  `SaveChangesAsync` (tracked mutation persists), and `PurgeOlderThanAsync` (deletes correct rows,
  returns count, returns 0 when nothing to delete).

## Tests

- `ListWebhookAuditHandlerTests.cs` — row ordering, event/status filtering, take-clamp at 200 (now
  exercised through the real repository).
- `GetWebhookAuditEntryHandlerTests.cs` — found/404 mapping (through the real repository).
- `ReplayWebhookEventHandlerTests.cs` — dispatch + replay-count increment + "no new row created",
  404 mapping, malformed-JSON handling (through the real repository).
- `SmartsuppWebhookAuditCleanupJobTests.cs` — 7-day retention deletion, job metadata (through the
  real repository).
- `SmartsuppWebhookAuditRepositoryTests.cs` — `CreateAsync` (generated id persists),
  `UpdateOutcomeAsync` (status/duration/processedAt), `ListAsync` (ordering, filtering, skip/take),
  `GetByIdAsync` (found/null), `GetForReplayAsync` (tracked entity, mutation persists via
  `SaveChangesAsync`), `PurgeOlderThanAsync` (deletes only stale rows and returns count, returns 0
  when nothing to delete).

## How to verify

```bash
cd backend
dotnet build
dotnet format --verify-no-changes
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Smartsupp.WebhookAudit"
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~PersistenceModuleTests"
grep -rn "ISmartsuppWebhookAuditWriter\|SmartsuppWebhookAuditWriter" .   # expect no output
```

Results in this session: `dotnet build` succeeded (0 errors); `dotnet format --verify-no-changes`
reported no changes in any file this task touched (a pre-existing, unrelated whitespace issue in
`backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs` — introduced
by commit `5b5b0d9`, before this task, and not touched here — is the only remaining `dotnet format`
finding); `Smartsupp.WebhookAudit` filter: 21/21 passed; `PersistenceModuleTests` filter: 6/6 passed
(confirms `AddPersistenceServices_RegistersNoRepositoryBindings` still holds — the new repository is
bound in `SmartsuppModule`, not `PersistenceModule`); the writer grep returned nothing.

## Notes

- Sandbox note (not a code concern): in this execution environment, running two `dotnet test`
  invocations concurrently against overlapping projects deadlocked on an MSBuild node named-pipe
  handshake. Killing the stuck processes and re-running sequentially with
  `DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 -m:1` avoided the deadlock and both
  filters completed normally. No code or config change was made because of this — it is purely a
  characteristic of running parallel `dotnet test` in this sandbox.
- The `backend/tools/Anela.Heblo.AccessMatrixGen` pre-build step throws and exits with code 134
  during every build in this sandbox (pre-existing, unrelated to this task); MSBuild treats it as a
  non-fatal warning (MSB3073) and the build still succeeds, as it did before this change.
- No deviations from the task-context spec: all new/replaced files match the provided code
  near-verbatim, and all four existing test files were migrated exactly as instructed.

## PR Summary

Replaces the ad-hoc `ISmartsuppWebhookAuditWriter` (create/update-outcome only, the sole
`Anela.Heblo.Persistence`-namespaced interface leaking into `Anela.Heblo.API`) and the direct
`ApplicationDbContext` injections scattered across `ListWebhookAuditHandler`,
`GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`, and `SmartsuppWebhookAuditCleanupJob`
with a single `ISmartsuppWebhookAuditRepository` (Domain) / `SmartsuppWebhookAuditRepository`
(Persistence) pair, bound in `SmartsuppModule.cs` and mirroring the existing
`ISmartsuppPresenceRepository` pattern (ADR-004: repository contracts in
`Anela.Heblo.Domain/Features/{Feature}`, implementations in `Anela.Heblo.Persistence/{Feature}`,
DI wiring in the feature module rather than `PersistenceModule`). `SmartsuppWebhookController` now
depends only on Domain/Application types for the audit table, closing the last `Anela.Heblo.API` ->
`Anela.Heblo.Persistence` leak for this feature. DTO projection for the list endpoint moved from an
inline EF `.Select(...)` into the handler, since the repository now returns domain entities — this
was an approved, deliberate change (Specification Amendment #2 in the task brief) with no change to
the wire contract. All four pre-existing handler/job unit tests were migrated to construct a real
`SmartsuppWebhookAuditRepository` around the in-memory `ApplicationDbContext` rather than exercising
the context directly, and the old writer's unit tests were renamed into
`SmartsuppWebhookAuditRepositoryTests.cs` with new coverage for `ListAsync`, `GetByIdAsync`,
`GetForReplayAsync`, and `PurgeOlderThanAsync`. No schema, HTTP contract, or observable behavior
change.

### Changes

- New: `ISmartsuppWebhookAuditRepository.cs`, `SmartsuppWebhookAuditRepository.cs`
- Deleted: `ISmartsuppWebhookAuditWriter.cs`, `SmartsuppWebhookAuditWriter.cs`
- Modified: `SmartsuppModule.cs`, `ListWebhookAuditHandler.cs`, `GetWebhookAuditEntryHandler.cs`,
  `ReplayWebhookEventHandler.cs`, `SmartsuppWebhookAuditCleanupJob.cs`,
  `SmartsuppWebhookController.cs`
- Migrated tests: `ListWebhookAuditHandlerTests.cs`, `GetWebhookAuditEntryHandlerTests.cs`,
  `ReplayWebhookEventHandlerTests.cs`, `SmartsuppWebhookAuditCleanupJobTests.cs`
- Renamed + extended: `SmartsuppWebhookAuditWriterTests.cs` -> `SmartsuppWebhookAuditRepositoryTests.cs`

## Status
DONE
