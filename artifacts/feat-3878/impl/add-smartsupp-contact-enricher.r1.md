# Implementation: add-smartsupp-contact-enricher

## What was implemented

Satisfied FR-3 from `spec.r1.md`: added a new `ISmartsuppContactEnricher` / `SmartsuppContactEnricher`
that resolves a `SmartsuppConversation.ContactId` against the local `SmartsuppContacts` table via a new
`ISmartsuppRepository.ContactExistsAsync` method, fetching-and-staging the contact via
`ISmartsuppApiClient.GetContactAsync` only when it is not already known locally, and clearing
`ContactId` (fail-open) on any REST failure or null result. This mirrors the pre-existing
`SmartsuppRepository.TryFetchAndStageContactAsync` behavior verbatim, including the load-bearing
`DateTimeKind.Utc` comment/logic and the broad `catch (Exception)` fail-open contract. The new class
is registered in DI but not yet wired into any call site — it is inert until Task 2
(`wire-reactions-to-contact-enricher`), per the task scope.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs` —
  new file: `ISmartsuppContactEnricher` interface + `SmartsuppContactEnricher` implementation
  (`EnrichContactAsync`, internal `MapContactDataToEntity`).
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` — added
  `Task<bool> ContactExistsAsync(string contactId, CancellationToken cancellationToken)` to the
  interface.
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — added the
  `ContactExistsAsync` implementation (`AsNoTracking().AnyAsync(...)` against `SmartsuppContacts`).
  Nothing removed from this file yet (that is Task 3's job).
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` — registered
  `services.AddScoped<ISmartsuppContactEnricher, SmartsuppContactEnricher>();` and added the
  `Anela.Heblo.Application.Features.Smartsupp.Infrastructure` using.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs` — new file,
  5 unit tests for `SmartsuppContactEnricher` (see below).
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs` — **not listed
  in the task's file list, but required**: the file's `NoOpSmartsuppRepository` test fake implements
  `ISmartsuppRepository` directly, so it needed a `ContactExistsAsync` stub (returns `false`) to keep
  the interface addition from breaking compilation. This is the only other `ISmartsuppRepository`
  implementer in the codebase (verified via `grep -rln "class.*: ISmartsuppRepository" backend/test`);
  all other test usages are `Mock<ISmartsuppRepository>`, which don't need updating.

## Tests

`backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs` — 5 tests, ported
from `SmartsuppRepositoryUnknownContactFetchTests.cs` onto the new class with `ISmartsuppRepository`
mocked instead of a live `ApplicationDbContext`:

1. `EnrichContactAsync_FetchesContactViaRest_WhenLocalContactMissing` — REST fetch + upsert + name/email
   backfill happens when `ContactExistsAsync` returns false.
2. `EnrichContactAsync_WipesContactId_WhenRestReturnsNull` — fail-open when REST returns null; no upsert.
3. `EnrichContactAsync_WipesContactId_WhenRestThrows` — fail-open when REST throws; no upsert.
4. `EnrichContactAsync_DoesNotCallRest_WhenContactAlreadyKnownLocally` — strict-mocked API client,
   confirms no REST call when `ContactExistsAsync` returns true.
5. `EnrichContactAsync_ReturnsUnchanged_WhenContactIdIsNull` — strict-mocked repository and API client,
   confirms a no-op short-circuit when `ContactId` is null.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~SmartsuppContactEnricherTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Smartsupp"
```

Results:
- `dotnet build Anela.Heblo.sln`: **Build succeeded, 0 Error(s)** (1 pre-existing, unrelated warning:
  MSB3073 from the `Anela.Heblo.AccessMatrixGen` post-build tool, present before this change too).
- `SmartsuppContactEnricherTests` filter: **Passed! - Failed: 0, Passed: 5, Total: 5**.
- Full `Smartsupp` filter: **207 Passed, 12 Failed, Total 219**. All 12 failures are pre-existing
  `SmartsuppRepositoryUpsertIntegrationTests` / `SmartsuppPresenceRepositoryIntegrationTests` cases that
  require a Postgres Testcontainers container; they fail with `System.ArgumentException: Docker is
  either not running or misconfigured` because the sandbox has no reachable Docker daemon. None of the
  12 touch `ISmartsuppContactEnricher` or the modified files' non-integration behavior — this is an
  environment limitation, not a regression from this change.

Note: `dotnet test` (which does its own build) intermittently hung indefinitely partway through
building `Anela.Heblo.API` — its post-build `AccessMatrixGen` code-gen step throws an unrelated,
pre-existing `JsonException` (`'/' is an invalid start of a value`) which `dotnet build` tolerates as a
warning (MSB3073) but which appears to wedge `dotnet test`'s own build phase in this sandbox. Working
around it by running `dotnet build Anela.Heblo.sln` first (fast, ~10-40s, succeeds cleanly) and then
`dotnet test --no-build` avoided the hang and is not a change to any Smartsupp code — noting it here
since a future revision or CI run may hit the same thing if it uses plain `dotnet test` without
`--no-build`.

## Notes

- Followed the task-context's explicit guidance to use a real DB existence check (`ContactExistsAsync`)
  rather than the incoming DTO's `ContactName`/`ContactEmail` fields, to avoid silently starving the
  `SmartsuppContacts` table when Smartsupp inlines name/email on the webhook event.
- Preserved the load-bearing `DateTimeKind.Utc` comment verbatim when moving `MapContactDataToEntity`.
- Kept the broad `catch (Exception)` around the REST call (not narrowed to `HttpRequestException`),
  matching the existing fail-open contract.
- Added an explicit warning log for the "REST returned null" path (today's `SmartsuppRepository` only
  warns on the exception path) — this was explicitly called out in the task context as an intentional,
  in-scope, logging-only improvement mandated by spec FR-3 step 4, not a behavior change to persisted
  data.
- This task does not change any existing call site (`SmartsuppRepository.TryFetchAndStageContactAsync`
  and its call sites are untouched); the new class is dead code from a runtime-behavior perspective
  until Task 2 wires it in.

## PR Summary
Adds `ISmartsuppContactEnricher`, a new Application-layer service that resolves a Smartsupp
conversation's contact against the local database and falls back to fetching-and-staging it from the
Smartsupp REST API only when necessary — mirroring the existing REST-staging logic inside
`SmartsuppRepository` but decoupled from persistence, as the first step of moving that REST dependency
out of the repository (issue #3878). The class is registered in DI in this PR but not yet used anywhere;
a follow-up task wires it into the conversation-upsert reaction pipeline.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs` — new `ISmartsuppContactEnricher` / `SmartsuppContactEnricher`
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` — added `ContactExistsAsync`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — implemented `ContactExistsAsync`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs` — DI registration
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs` — new unit tests
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppWebhookControllerTests.cs` — added missing `ContactExistsAsync` stub to `NoOpSmartsuppRepository` test fake (required to keep the interface addition compiling)

## Status
DONE
