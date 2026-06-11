# Specification: Unit tests for GetUserEffectivePermissionsHandler

## Summary
Add unit-test coverage for `GetUserEffectivePermissionsHandler` in the Authorization slice. The handler currently has zero direct tests despite gating effective-permission resolution, including the security-critical `IsActive` short-circuit that denies all permissions to deactivated users. This work adds a focused xUnit test class that locks in the three observable behaviors of the handler against a mocked repository.

## Background
`GetUserEffectivePermissionsHandler` (`backend/src/Anela.Heblo.Application/Features/Authorization/UseCases/GetUserEffectivePermissions/GetUserEffectivePermissionsHandler.cs`) is the use case the Authorization module exposes to compute the effective permission list for a given user id. It is invoked through MediatR and depends on `IAuthorizationRepository` for user lookup and the full group graph (group→permission and group→parent edges). The handler then delegates transitive closure to `GroupClosure.Resolve` and merges the seed permission `AccessRoles.Base` before returning a sorted list.

The handler has three logical branches:
1. `GetUserByIdAsync` returns `null` → response with `ErrorCodes.AuthorizationUserNotFound`.
2. `user.IsActive == false` → response with an empty `Permissions` list and no error code (silent deny).
3. Active user → resolve closure of `UserGroups → GroupId`, union with `AccessRoles.Base`, return distinct list ordered by `OrderBy(p => p)`.

Branch (2) is the only barrier that prevents a deactivated user from retaining a previously assigned permission set. There is no test today that fails if the `!user.IsActive` guard is removed, inverted, or accidentally short-circuited. The weekly coverage-gap routine identified this on 2026-06-08 (CI run #27104028537, commit 6568feba) and filed it because the regression mode is silent — production keeps responding 200, just with the wrong permissions.

Existing Authorization-module tests live under `backend/test/Anela.Heblo.Tests/Authorization/` and follow xUnit + Moq + FluentAssertions conventions (see `GetMeHandlerTests.cs` as the closest stylistic reference).

## Functional Requirements

### FR-1: Test class wiring
A new test class `GetUserEffectivePermissionsHandlerTests` is added under `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`. It uses xUnit (`[Fact]`), FluentAssertions, and Moq, matching the rest of the Authorization test folder.

**Acceptance criteria:**
- File path: `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`.
- Namespace: `Anela.Heblo.Tests.Authorization`.
- Class is `public` and contains at least the three `[Fact]` methods defined in FR-2..FR-4.
- All tests pass with `dotnet test --no-build` from the worktree root.
- Tests follow Arrange / Act / Assert with descriptive method names (`MethodUnderTest_State_ExpectedResult` style used elsewhere in the folder).

### FR-2: User-not-found path returns AuthorizationUserNotFound
Verify that when `IAuthorizationRepository.GetUserByIdAsync` returns `null`, the handler returns a response whose error code is `ErrorCodes.AuthorizationUserNotFound` and which has no permissions populated.

**Acceptance criteria:**
- Arrange: `Mock<IAuthorizationRepository>` returns `(AppUser?)null` for the request's `UserId`.
- Act: invoke `Handle` with a `GetUserEffectivePermissionsRequest { UserId = <some Guid> }`.
- Assert: `response.Success == false` and `response.ErrorCode == ErrorCodes.AuthorizationUserNotFound` (use whatever the base `BaseResponse` exposes — assert against the same field used by `GetMeHandlerTests` and siblings; if `ErrorCode` is the property, assert it equals `ErrorCodes.AuthorizationUserNotFound`).
- Assert: `GetGroupGraphAsync` was NEVER called (the handler must not load the graph for a non-existent user). Use `Mock.Verify(..., Times.Never)`.

### FR-3: Inactive-user path returns empty permission list silently (security-critical)
Verify that when the user exists but `IsActive == false`, the handler returns a response with an empty `Permissions` list and no error code. Even if the user has groups assigned, none of them must be resolved into permissions, and `AccessRoles.Base` must NOT be included.

**Acceptance criteria:**
- Arrange: repository returns an `AppUser` with `IsActive = false` and a non-empty `UserGroups` collection (at least one `UserGroup` with an arbitrary `GroupId`) so the test would catch a regression that forgets the guard.
- Act: invoke `Handle`.
- Assert: `response.Permissions` is not null and is empty (`Permissions.Should().BeEmpty()`).
- Assert: `response.Success == true` (no error code set — the inactive path is intentionally silent per the current implementation).
- Assert: `GetGroupGraphAsync` was NEVER called (`Times.Never`). This pins down the short-circuit behavior — if a future refactor moves the graph load before the `IsActive` check, this test fails.
- Assert: `Permissions` does NOT contain `AccessRoles.Base`. This is the explicit anti-regression assertion for the security guard.

### FR-4: Active-user path returns merged, deduplicated, sorted permissions including AccessRoles.Base
Verify that for an active user with one or more group memberships, the handler:
- Resolves the group closure via `GroupClosure.Resolve` (transitively through `GroupParent` edges),
- Unions the resolved permissions with `AccessRoles.Base`,
- Deduplicates,
- Returns the result ordered ascending by ordinal string comparison.

**Acceptance criteria:**
- Arrange: repository returns an `AppUser` with `IsActive = true` and `UserGroups` containing one group `G1`.
- Arrange: `GetGroupGraphAsync` returns:
  - `GroupPermission` rows: `(G1, "permB")`, `(G1, "permA")`, `(G2, "permC")`.
  - `GroupParent` rows: `(G1, G2)` so closure pulls in `permC` from the parent.
- Act: invoke `Handle`.
- Assert: `response.Success == true`, no error code.
- Assert: `response.Permissions` equals exactly `[AccessRoles.Base, "permA", "permB", "permC"]` in that order (ordinal ascending). Use `Should().Equal(...)` to assert order, not `BeEquivalentTo`.
- Assert: there are no duplicates if `AccessRoles.Base` is also produced by a group permission row (covered implicitly — if needed, add a small additional fact for the dedup edge: include `AccessRoles.Base` as a `GroupPermission` and confirm it appears only once).

### FR-5: Test data builders stay local to the test file
Do not introduce shared test fixtures or builders outside this file. Inline the construction of `AppUser`, `UserGroup`, `GroupPermission`, and `GroupParent` instances. This keeps the diff surgical and matches the style of `GetMeHandlerTests`.

**Acceptance criteria:**
- No new files outside `backend/test/Anela.Heblo.Tests/Authorization/GetUserEffectivePermissionsHandlerTests.cs`.
- No changes to existing production code under `backend/src/`.

## Non-Functional Requirements

### NFR-1: Performance
- Tests must run in under 200 ms per fact on a developer machine. The handler is pure CPU/in-memory with a mocked repository — no I/O, no DB, no `WebApplicationFactory`.
- Total addition to `dotnet test` wall time: < 1 s.

### NFR-2: Security
- The inactive-user test (FR-3) is treated as a security regression test. Its assertions must explicitly check both "empty permissions" AND "graph was not loaded" AND "AccessRoles.Base is absent". Removing any one of these weakens the guard.
- Tests must not require, log, or contain real Entra object ids, emails, or tokens. Use placeholder values (`"oid-x"`, `"u@x.cz"`) consistent with sibling tests.

### NFR-3: Maintainability
- Follow the coding conventions enforced in the rest of the test folder: xUnit `[Fact]`, FluentAssertions, Moq with default loose behavior (or `MockBehavior.Strict` only where verifying "never called" interactions).
- Test method names describe behavior, not implementation (e.g. `Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph`).
- No comments explaining what well-named asserts already convey.

## Data Model
No data-model changes. Tests construct in-memory instances of existing types only:
- `AppUser` (`backend/src/Anela.Heblo.Domain/Features/Authorization/Entities/AppUser.cs`) — set `Id`, `Email`, `DisplayName`, `IsActive`, `UserGroups`.
- `UserGroup`, `PermissionGroup`, `GroupPermission`, `GroupParent` (siblings in `Entities/`).
- `AccessRoles` (`backend/src/Anela.Heblo.Domain/Features/Authorization/` — generated by AccessMatrixGen) — read `AccessRoles.Base` only.
- `ErrorCodes` (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`) — read `AuthorizationUserNotFound` only.

## API / Interface Design
No public API changes. The handler signature stays:

```csharp
Task<GetUserEffectivePermissionsResponse> Handle(
    GetUserEffectivePermissionsRequest request,
    CancellationToken ct);
```

Tests invoke `Handle` directly on a manually constructed `GetUserEffectivePermissionsHandler(repoMock.Object)` instance. No MediatR pipeline, no DI container.

## Dependencies
- xUnit 2.x (already in test project).
- Moq (already in test project — see `GetMeHandlerTests`).
- FluentAssertions (already in test project).
- No new NuGet packages.

## Out of Scope
- Integration tests through `WebApplicationFactory` — `AuthorizationIntegrationTests.cs` already covers the HTTP boundary broadly; this gap is specifically about handler-level isolation.
- Refactoring the handler to emit an explicit error code for the inactive path. The current behavior (silent empty list) is a deliberate product decision and is what these tests pin down. Changing that behavior is a separate work item.
- Tests for `GroupClosure.Resolve` itself (cycle handling, diamond handling). The handler tests only need a small graph that exercises the union/order/dedup contract.
- Tests for the request/response DTOs.
- Adding `[Theory]` parameterization — three discrete `[Fact]`s are clearer for these branches.

## Open Questions
None.

## Status: COMPLETE
