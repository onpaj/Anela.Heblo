# Plan: Remove dead `IGraphService.SearchUsersAsync`

## Summary

`IGraphService.SearchUsersAsync` is implemented in both `GraphService` and `MockGraphService` but has zero production consumers — no handler, endpoint, MCP tool, or frontend code calls it. This plan removes the method, its two implementations, and its dedicated unit test file, restoring the interface to only what is actually used. This is a pure deletion (Option A from the finding); no new functionality is added.

## Context

A codebase-wide grep confirms the finding precisely:
- `IGraphService.SearchUsersAsync` (`backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs:14`) has no callers outside its own implementations and tests.
- By contrast, the interface's other two methods are actively used: `GetGroupMembersAsync` is called from `GraphArticleUserResolver` and `GetGroupMembersHandler`; `GetAppRoleMembersAsync` is called from `EntraAccessUserSourceAdapter`. This confirms the interface itself is legitimate and only this one method is dead.
- `SearchUsersAsync` is exercised only by `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` (120 lines, 5 tests), which tests the method in isolation and would become meaningless once the method is removed.

Per the finding, YAGNI/ISP guidance in `docs/architecture/development_guidelines.md` and this being a solo-maintained codebase both favor deleting now and re-adding when a real consumer (handler/endpoint/MCP tool) is built, rather than carrying dead weight speculatively.

## Functional requirements

**FR-1: Remove `SearchUsersAsync` from `IGraphService`**
- Delete the method declaration at `IGraphService.cs:14`.
- Acceptance: `IGraphService` contains only `GetGroupMembersAsync` and `GetAppRoleMembersAsync`.

**FR-2: Remove the `GraphService` implementation**
- Delete the `SearchUsersAsync` method body (`GraphService.cs:192–265`, ~75 lines) including its Graph `$search` query construction, quote-stripping, caching-adjacent constants only used by it (verify `SearchResultLimit` isn't reused elsewhere before removing), and associated using directives if they become unused.
- Acceptance: `GraphService` no longer defines `SearchUsersAsync`; `SearchResultLimit` constant removed only if unused elsewhere.

**FR-3: Remove the `MockGraphService` implementation**
- Delete the `SearchUsersAsync` method (`MockGraphService.cs:22–26`).
- Acceptance: `MockGraphService` no longer defines `SearchUsersAsync`.

**FR-4: Remove the now-orphaned unit tests**
- Delete `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` in full (it exclusively tests the removed method).
- Check `MockGraphServiceTests.cs` and any other test file for a `SearchUsersAsync`-related test case and remove it if present (grep showed no hits there currently, but re-verify at implementation time since test files may have been added since this plan was written).
- Acceptance: `dotnet build` succeeds with no leftover references; no orphaned mocks/setups referencing `SearchUsersAsync` remain in any test file.

## Non-functional requirements

- No behavior change to any existing endpoint, handler, or MCP tool — this is a pure subtraction.
- No performance or security implications; removing unused code cannot regress either.

## Data model

No data model changes. `UserDto` (the return type) remains unchanged since it's still used by the two surviving methods.

## Interfaces

No API endpoints, MediatR requests, or MCP tools are affected — none existed for this method, which is exactly the problem being fixed.

## Dependencies and scope

**In scope:**
- `IGraphService.cs`
- `GraphService.cs`
- `MockGraphService.cs`
- `GraphServiceSearchTests.cs` (delete)
- Any other test file with a stray `SearchUsersAsync` mock setup (verify at implementation time)

**Out of scope:**
- Building the search feature (Option B from the finding) — explicitly not requested; if user directory search becomes a real requirement later, it should go through a fresh brainstorm/spec cycle with a proper vertical slice (handler, endpoint/MCP tool, frontend).
- Any other `IGraphService` methods or Graph API integration behavior.

## Rough plan

1. Delete the `SearchUsersAsync` declaration from `IGraphService.cs`.
2. Delete the `SearchUsersAsync` implementation from `GraphService.cs`; remove `SearchResultLimit` only if grep confirms no other usage.
3. Delete the `SearchUsersAsync` implementation from `MockGraphService.cs`.
4. Delete `GraphServiceSearchTests.cs`; grep test suite for any remaining `SearchUsersAsync` references and clean up.
5. Run `dotnet build` and `dotnet format` per repo validation rules; run the full `Anela.Heblo.Tests` suite to confirm nothing else depended on the method (e.g., via reflection or DI registration checks).
6. Grep the whole repo one more time for `SearchUsersAsync` to confirm zero remaining references before calling this done.

## Open questions

- **`SearchResultLimit` constant reuse**: not verified in this planning pass whether it's referenced elsewhere in `GraphService.cs`. Default: check at implementation time and only remove if truly unused (surgical-changes rule — don't remove things still in use).
- **Test coverage tooling gates**: repo has no stated minimum coverage threshold in the docs reviewed; assuming removing 5 dead-method tests won't trip any coverage gate. If a coverage gate exists and fails, flag it rather than adding filler tests.
