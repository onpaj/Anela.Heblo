# Review: Stop `GetAppRoleMembersAsync` from swallowing Graph failures

## Verdict: done

## Method

Read plan-01.md, design-01.md, architecture-01.md, and development-01.md, then diffed every
changed file against `main`/HEAD~1 and read the resulting source in full (not just development-01's
description of it) to verify the description matches reality.

## Conformance to plan (FR-1 through FR-5)

- **FR-1** (`GraphService.cs:290-300`): token-acquisition catch narrowed to `catch (MsalException
  msalEx)`, throws `GraphServiceAuthException` with the same message shape as
  `GetGroupMembersAsync:167-173`. Matches.
- **FR-2** (`GraphService.cs:450-458`): outer catch-all no longer returns `[]`. A `catch
  (GraphServiceAuthException) { throw; }` passthrough (avoids double-logging what FR-1 already
  logged) followed by `catch (Exception ex) { log; throw; }` for genuinely unexpected failures —
  same log-then-rethrow shape as `GetGroupMembersAsync`'s outer catch. Verified via a new test
  (`GetAppRoleMembersAsync_TransportThrows_Throws`) that a transport-level `HttpRequestException`
  now propagates instead of being swallowed.
- **FR-3** (five early-return branches at lines 283-287, 310-314, 317-321, 337-341, 354-358,
  413-417): untouched, confirmed by diff. The existing
  `GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError` test is unmodified and
  still asserts `[]` for the `$batch` HTTP-failure branch — correctly resolves plan Open Question 1
  (this test exercises the FR-3-preserved branch, not the outer catch-all removed by FR-2; a
  separate new test covers the outer-catch-all case).
- **FR-4** (`IGraphService.cs`): `<exception cref="GraphServiceAuthException">` xmldoc added to
  `GetAppRoleMembersAsync`, matching the existing doc on `GetGroupMembersAsync`.
- **FR-5**: implemented, but correctly *relocated* per architecture-01.md's correction rather than
  as plan-01.md/design-01.md originally specified — see below.

## Conformance to architecture correction (component 3/4 supersession)

architecture-01.md required moving exception translation out of `GetEntraAccessUsersHandler`
(which would have created an Authorization → UserManagement module-boundary leak) and into
`EntraAccessUserSourceAdapter`, the pre-existing designated adapter boundary. Verified this was
followed exactly, not the original design-01.md version:

- Two new Authorization-owned exception types (`EntraAccessSourceAuthException`,
  `EntraAccessSourceException`) added under `Authorization/Contracts/`, matching the doc's
  specified shape.
- `EntraAccessUserSourceAdapter.GetBaseMembersAsync` now wraps `_graph.GetAppRoleMembersAsync` in a
  try/catch translating `GraphServiceAuthException` → `EntraAccessSourceAuthException` and
  `GraphServiceException` → `EntraAccessSourceException`; `UnauthorizedAccessException` deliberately
  left uncaught (verified by a dedicated adapter test that it propagates unhandled) — this matches
  the architecture doc's risk analysis about the nested `GetGroupMembersAsync` group-expansion call
  at `GraphService.cs:385`.
- `GetEntraAccessUsersHandler` only references `Authorization.Contracts` types (plus
  `Application.Shared` for `ErrorCodes` and `Microsoft.Extensions.Logging`) — no import of
  anything under `UserManagement`. Confirmed via diff: no such using was added.
- A new `ModuleBoundariesTests` rule (`Authorization -> UserManagement`, empty allowlist) was added,
  exactly as the architecture doc's "recommended, cheap to include" mitigation. Verified the rule's
  direction is correct against the existing `Consumer_types_should_not_reference_provider_owned_namespaces`
  mechanism (`ModuleBoundariesTests.cs:642-686`): it inspects types under
  `Anela.Heblo.Application.Features.Authorization` for references into
  `Anela.Heblo.Application.Features.UserManagement` (among others) — `EntraAccessUserSourceAdapter`
  lives in the `UserManagement` namespace and is therefore correctly outside this rule's inspected
  set, so the legitimate adapter boundary isn't flagged.

## Correctness checks

- `GraphServiceAuthException`/`GraphServiceException` constructors (`(string, Exception)`) match all
  new call sites.
- `EntraAccessUserSourceAdapter` is `internal sealed`; `Anela.Heblo.Application`'s
  `AssemblyInfo.cs`/`.csproj` already grant `InternalsVisibleTo("Anela.Heblo.Tests")`, so the new
  `EntraAccessUserSourceAdapterTests.cs` referencing it directly compiles.
  `ErrorCodes.ConfigurationError` and `ErrorCodes.ExternalServiceError` both already exist in
  `ErrorCodes.cs`. `GetEntraAccessUsersResponse.Users` defaults to `new()`, so the
  `result.Users.Should().BeEmpty()` assertions on the new failure-path handler tests hold without
  the handler needing to set `Users` explicitly on the error branches.
- `ThrowingHttpMessageHandler`, used by the new `GetAppRoleMembersAsync_TransportThrows_Throws`
  test, is a pre-existing test helper already used elsewhere in the same file (line 513) — not a
  new untested fixture.
- `MsalUiRequiredException("err", "msg")`, used by the new token-acquisition test, is the same
  construction pattern already used in three other places in this test project
  (`GraphServiceSearchTests.cs`, `GraphPlannerServiceTests.cs`, and the existing
  `GetGroupMembersAsync_TokenAcquisitionMsalException_Throws` test).
- No dead code left behind: the old `return new List<UserDto>()` swallow sites for FR-1/FR-2 were
  fully replaced, not left unreachable alongside the throw.

## Completeness

All required tests from the plan and architecture doc are present: token-acquisition-failure test,
outer-catch-all-now-propagates test, FR-3-preserved-branch test (unchanged), adapter translation
tests (auth exception, generic exception, `UnauthorizedAccessException` passthrough, happy-path
mapping regression), and handler tests for both new error-mapping branches. `git diff --stat`
confirms exactly the file set architecture-01.md's implementation guidance specified (8 modified, 3
new — plus `development-01.md` and this review file, which aren't code).

## Not independently re-verified

`dotnet build` / `dotnet format` / `dotnet test` could not be run — this sandbox has no .NET SDK and
no Docker/Podman, the same limitation development-01.md already disclosed. I instead manually
traced every new/changed symbol reference (exception types, `ErrorCodes` members, constructor
signatures, `InternalsVisibleTo`, existing test helpers) against the current source to confirm the
code as written should compile and the described test behavior is accurate. CI's standard
build/format/test step must still run before merge, per this repo's own validation checklist.

## Non-binding notes (not blocking)

- The new outer `catch (Exception ex) { log; throw; }` in `GetAppRoleMembersAsync` will also log
  (with the generic "Unexpected error fetching app role members" message) an `UnauthorizedAccessException`
  bubbling up from the nested `GetGroupMembersAsync` group-expansion call, whereas
  `GetGroupMembersAsync` itself has a dedicated `catch (UnauthorizedAccessException authEx)` with a
  more specific log message before its own catch-all. Purely a log-message-wording difference, not
  a behavior or correctness issue — the exception still propagates unhandled either way.
