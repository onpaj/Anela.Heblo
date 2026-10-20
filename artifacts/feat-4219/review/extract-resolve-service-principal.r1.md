# Code Review: extract-resolve-service-principal (feat-4219)

## Summary

The implementation extracts `GetAppRoleMembersAsync`'s Step 1 (service
principal resolution) into a new private `ResolveServicePrincipalAsync`
helper exactly as specified in the task context, moving the HTTP call, error
handling, and logging verbatim with no behavior change. The diff matches the
task-context's Step 2 and Step 3 code blocks character-for-character. Build
succeeds and the full 21-test suite covering this method (`GetAppRoleMembersTests`
+ `GraphServiceTests`) passes both before and after the change.

## Review Result: PASS

### task: extract-resolve-service-principal
**Status:** PASS

## Docs to Update

(None — internal private-method refactor only, no change to `IGraphService`'s
public contract, configuration, or documented behavior.)

## Overall Notes

- Correctness: the new helper returns `(null, null)` on both failure paths
  (non-success HTTP status, missing/empty `id`), exactly mirroring the original
  inline early-returns in `GetAppRoleMembersAsync` (which returned an empty
  `List<UserDto>` in both cases). The caller's `if (spId is null) return new
  List<UserDto>();` preserves this.
- The `appRoles` JsonElement is explicitly `.Clone()`d before the source
  `JsonDocument` (`spDoc`, scoped with `using` inside the helper) goes out of
  scope, which is required — `JsonElement` values backed by a disposed
  `JsonDocument` throw `ObjectDisposedException` on access. This is correctly
  handled and commented.
- Step 2 (finding `appRoleId`) was correctly left inline per the task context,
  only updated to read `appRoles.Value.EnumerateArray()` instead of the
  removed `spDoc`/`appRolesEl` locals — no premature extraction beyond what
  this task asked for.
- Test coverage: no new tests were added, but none were required — this is a
  pure refactor of a private code path, already fully exercised by the
  existing `GetAppRoleMembersTests` (happy path, cache hit, missing client id)
  and the broader `GraphServiceTests` suite. Test count is unchanged (21
  before, 21 after), matching the task's own acceptance criteria.
- Two minor, non-blocking observations from the implementer, already noted in
  the impl artifact and not spec violations: (1) the task doc's `dotnet build
  backend/Anela.Heblo.sln` path is stale (the sln is at the repo root); (2)
  the task doc's "12 PASS" expectation is stale (actual baseline is 21 PASS).
  Neither affects spec compliance — the task's real acceptance criterion
  ("same count as Step 1 baseline") is met.
