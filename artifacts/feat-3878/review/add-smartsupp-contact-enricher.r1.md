# Code Review: add-smartsupp-contact-enricher

## Summary

The implementation satisfies FR-3 exactly as specified in the task context: a new
`ISmartsuppContactEnricher` resolves a conversation's `ContactId` against a real DB existence check
(`ContactExistsAsync`), fetches-and-stages via REST only when needed, and fails open (clears
`ContactId`) on REST error or null result. The load-bearing `DateTimeKind.Utc` comment and the broad
`catch (Exception)` fail-open contract were preserved verbatim as required. All 9 implementation steps
from the task were completed, including the one necessary deviation (fixing a test fake to keep the
interface addition compiling), which was disclosed and justified in the impl notes.

## Review Result: PASS

### task: add-smartsupp-contact-enricher
**Status:** PASS

Verified against `task-context/add-smartsupp-contact-enricher.md`:

- **Step 1/2 (`ContactExistsAsync`)** — added to `ISmartsuppRepository` and implemented in
  `SmartsuppRepository` exactly as specified (`AsNoTracking().AnyAsync(c => c.Id == contactId, ...)`).
- **Step 3 (build + grep check)** — confirmed independently: `dotnet build Anela.Heblo.sln` succeeds
  with 0 errors; `grep -rln "ISmartsuppRepository" backend/src | grep -v Reactions` shows only
  consumers, matching the expected outcome.
- **Step 4 (`ISmartsuppContactEnricher`)** — new file matches the task's exact specification: DB
  existence check before any REST call, broad `catch (Exception)` around `GetContactAsync` clearing
  `ContactId` and logging a warning, an added warning log on null REST result (the deliberate,
  logging-only improvement called out in the task context as required by spec FR-3 step 4), and the
  `MapContactDataToEntity` helper carries the load-bearing `DateTimeKind.Utc` comment forward verbatim.
  `ContactName`/`ContactEmail` are only backfilled with `??=` (does not clobber pre-existing values),
  matching the intent of "the FK link survives and the conversation row carries the display name."
- **Step 5 (DI registration)** — `SmartsuppModule.cs` registers `ISmartsuppContactEnricher` right after
  `ISmartsuppRepository`, with the using added in the correct alphabetical position.
- **Step 6 (tests)** — `SmartsuppContactEnricherTests.cs` contains all 5 specified test cases, matching
  the task's provided test code exactly (REST-fetch-and-upsert path, REST-null fail-open, REST-throws
  fail-open, already-known-locally short-circuit with a strict mock proving no REST call, and
  null-`ContactId` short-circuit with strict mocks proving no calls at all).
- **Step 7/8 (test + build verification)** — independently re-ran:
  `dotnet build Anela.Heblo.sln` → 0 errors. `dotnet test ... --filter SmartsuppContactEnricherTests`
  → 5/5 passed. `dotnet test ... --filter Smartsupp` → 207 passed, 12 failed; all 12 failures are
  pre-existing `SmartsuppRepositoryUpsertIntegrationTests` / `SmartsuppPresenceRepositoryIntegrationTests`
  cases failing on `System.ArgumentException: Docker is either not running or misconfigured` —
  Testcontainers-backed integration tests with no reachable Docker daemon in this sandbox, unrelated to
  the new code and pre-existing regardless of this change. No test touching `ISmartsuppContactEnricher`,
  `ISmartsuppRepository`, or `SmartsuppRepository`'s non-integration behavior failed.
- **Unlisted-but-necessary fix**: `SmartsuppWebhookControllerTests.cs`'s `NoOpSmartsuppRepository` test
  fake directly implements `ISmartsuppRepository` and was not caught by the task's `grep -rln
  "ISmartsuppRepository" backend/src` check (scoped to `backend/src`, not `backend/test`). The developer
  correctly found and fixed this build break (added a trivial `ContactExistsAsync` stub returning
  `false`) and disclosed it clearly in the impl notes rather than silently expanding scope. This is the
  right call — leaving it broken would have failed Step 8's full-build acceptance criterion.
- **Scope discipline** — no existing call site was touched; `SmartsuppRepository.TryFetchAndStageContactAsync`
  and `MapContactDataToEntity` remain in place untouched, confirming the new class is correctly inert
  until Task 2 wires it in, per the task's explicit scope boundary.

No correctness bugs, no architecture violations, no missing required tests.

## Docs to Update

(none — this task adds an internal Application-layer service with no public behavior change; the class
is not yet wired into any call site, so there is nothing operator-facing to document yet)

## Overall Notes

- The impl notes flag that plain `dotnet test` (which performs its own build) intermittently hangs in
  this sandbox partway through `Anela.Heblo.API`'s post-build `AccessMatrixGen` step (a pre-existing,
  unrelated `JsonException` that `dotnet build` tolerates as warning MSB3073 but that appears to wedge
  `dotnet test`'s build phase). The workaround (`dotnet build` then `dotnet test --no-build`) is sound
  and doesn't touch any Smartsupp code; worth keeping in mind for later tasks/rounds in this same
  environment so time isn't lost re-diagnosing the same hang.
- Docker-dependent integration test failures (12) are an environment limitation of this sandbox, not a
  code defect — flagging for visibility but not blocking, consistent with reviewer guidance not to mark
  REVISION_NEEDED for infrastructure/tooling limitations outside the implementation's control.
