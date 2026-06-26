# Architecture Review: Unit tests for GetUserEffectivePermissionsHandler

## Skip Design: true

This is a test-only addition. No production code changes, no UI, no new endpoints, no infrastructure. The work consists of one new `.cs` test file inside an existing test project.

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions in the Authorization slice:

- The handler under test (`backend/src/.../GetUserEffectivePermissions/GetUserEffectivePermissionsHandler.cs`) depends only on `IAuthorizationRepository`, which is already trivially mockable via Moq (used in `GetMeHandlerTests`, `AddGroupMemberHandlerTests`, `AssignUserGroupsHandlerTests`, etc.).
- The test project `Anela.Heblo.Tests` already references `Anela.Heblo.Application`, `Anela.Heblo.Domain`, and `Anela.Heblo.Persistence` — verified via `SetUserCanPackHandlerTests.cs` which imports `Anela.Heblo.Persistence.Features.Authorization`. No new project references are needed; the test can resolve `IAuthorizationRepository`, `GroupClosure`, entity types, and `AccessRoles.Base`.
- The closest stylistic siblings (`GetMeHandlerTests.cs`, `SetUserCanPackHandlerTests.cs`) confirm the pattern: terse, inline Arrange in each `[Fact]`, no shared fixtures, FluentAssertions for asserts, Moq for the dependency.
- The chosen unit-test boundary (handler + mocked repo) is correct. Going through `WebApplicationFactory` would couple this test to MediatR pipeline, DI, claims transformation, and EF — none of which is what the coverage gap is about. The handler's three branches are pure in-memory logic; a unit test is the right granularity.

There are no conflicting patterns. The proposal does not introduce new abstractions, helpers, or fixtures.

## Proposed Architecture

### Component Overview

```
backend/test/Anela.Heblo.Tests/Authorization/
└── GetUserEffectivePermissionsHandlerTests.cs   [NEW]
        │
        ├── builds Mock<IAuthorizationRepository>
        ├── constructs GetUserEffectivePermissionsHandler directly
        ├── invokes handler.Handle(request, CancellationToken.None)
        └── asserts on GetUserEffectivePermissionsResponse
                       (Success, ErrorCode, Permissions)
```

No diagrams of production wiring change. The test stands alone.

### Key Design Decisions

#### Decision 1: Moq for the repository (not a fake or test double class)

**Options considered:**
1. `Mock<IAuthorizationRepository>` — Moq, same as `GetMeHandlerTests` and most sibling handler tests.
2. Hand-rolled `FakeAuthorizationRepository` implementing the interface.
3. EF Core InMemory + real `AuthorizationRepository` (as `SetUserCanPackHandlerTests` does).

**Chosen approach:** Moq, default loose behavior, with `Verify(..., Times.Never)` for the negative-interaction assertions on `GetGroupGraphAsync`.

**Rationale:** FR-2 and FR-3 require asserting that `GetGroupGraphAsync` was **not** called. Moq's `Verify(..., Times.Never())` is the cleanest mechanism for this. A hand-rolled fake would need an explicit call counter, which is dead weight. An InMemory DB would actually execute graph queries and cannot prove the short-circuit. Moq matches the prevailing style for pure-handler tests in this folder.

#### Decision 2: MockBehavior — Loose, not Strict

**Options considered:**
1. Default loose — unmocked calls return defaults; `Verify` is used to pin down "never called".
2. `MockBehavior.Strict` — every interaction must be pre-configured.

**Chosen approach:** Loose. Use `Verify(repo => repo.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never)` in FR-2 and FR-3.

**Rationale:** Strict mode makes the "graph was not loaded" assertion implicit (any unconfigured call would throw) but conflates two failure modes: a missing setup vs. a real regression. Explicit `Times.Never` makes the security-critical assertion in FR-3 visible at the assert site, which is exactly where a future reader will look. `GetMeHandlerTests` uses Strict on the resolver because it's expected to be unused for SuperUser — that pattern works there because there's only one method on the interface; here `IAuthorizationRepository` has 13 methods and we'd need many setups.

#### Decision 3: Inline test data construction (no builders)

**Chosen approach:** Construct `AppUser`, `UserGroup`, `GroupPermission`, `GroupParent` instances inline in each `[Fact]` using property initializers, mirroring `GetMeHandlerTests` and `GroupClosureTests`.

**Rationale:** Spec FR-5 explicitly requires this and the surrounding folder confirms it as the norm. A builder would obscure exactly what differs between the three test cases (it's the discriminating state — `IsActive`, the presence of `UserGroups`, the contents of the graph — and that should be visible at the call site).

#### Decision 4: Ordering assertion uses `Should().Equal(...)`, not `BeEquivalentTo`

**Chosen approach:** `response.Permissions.Should().Equal(new[] { AccessRoles.Base, "permA", "permB", "permC" })`.

**Rationale:** The handler's final `OrderBy(p => p).ToList()` is a contract the spec pins down. `BeEquivalentTo` would pass even if a future change accidentally returned an unsorted list. `Equal` enforces both content and order. (Note: `OrderBy(p => p)` on strings uses the default culture-aware comparer, not ordinal. For the ASCII-only test inputs in FR-4, ordinal and culture-aware ordering produce the same result, so the assertion is safe — but see Specification Amendments below.)

## Implementation Guidance

### Directory / Module Structure

One new file, no other changes:

```
backend/test/Anela.Heblo.Tests/Authorization/
  GetUserEffectivePermissionsHandlerTests.cs   [NEW]
```

Namespace: `Anela.Heblo.Tests.Authorization` (matches sibling files).

### Interfaces and Contracts

The test consumes existing public surface only:

```csharp
// Application layer
using Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;
using Anela.Heblo.Application.Shared;     // ErrorCodes

// Domain layer
using Anela.Heblo.Domain.Features.Authorization;            // IAuthorizationRepository, AccessRoles
using Anela.Heblo.Domain.Features.Authorization.Entities;   // AppUser, UserGroup, GroupPermission, GroupParent

// Test plumbing
using FluentAssertions;
using Moq;
using Xunit;
```

Key contract surface the tests bind to:

| Member | Signature | Why it matters |
|---|---|---|
| `IAuthorizationRepository.GetUserByIdAsync` | `Task<AppUser?> (Guid, CancellationToken)` | First call — drives branches (1) vs (2)/(3). |
| `IAuthorizationRepository.GetGroupGraphAsync` | `Task<(List<GroupPermission>, List<GroupParent>)> (CancellationToken)` | Only called on the active path. `Times.Never` proves the short-circuit. |
| `GetUserEffectivePermissionsResponse` | `bool Success`, `ErrorCodes? ErrorCode`, `List<string> Permissions` | Inherits `BaseResponse`. `ErrorCode` is the property name (verified). |
| `ErrorCodes.AuthorizationUserNotFound` | enum value 3202 | Confirmed in `ErrorCodes.cs:395`. |
| `AccessRoles.Base` | `const string = "heblo_user"` | Confirmed in `AccessRoles.generated.cs:6`. |

### Data Flow

**FR-2 (user not found):**
```
request{UserId} → repo.GetUserByIdAsync → null
                ↓
                return GetUserEffectivePermissionsResponse(ErrorCodes.AuthorizationUserNotFound)
                ↓
                Assert: Success==false, ErrorCode==AuthorizationUserNotFound
                Verify: GetGroupGraphAsync never called
```

**FR-3 (inactive user — security-critical):**
```
request{UserId} → repo.GetUserByIdAsync → AppUser{ IsActive=false, UserGroups=[{GroupId=G1}] }
                ↓
                return new GetUserEffectivePermissionsResponse { Permissions = new() }
                ↓
                Assert: Success==true, ErrorCode==null, Permissions empty
                Assert: Permissions does NOT contain AccessRoles.Base
                Verify: GetGroupGraphAsync never called
```

**FR-4 (active user):**
```
request{UserId} → repo.GetUserByIdAsync → AppUser{ IsActive=true, UserGroups=[{GroupId=G1}] }
                → repo.GetGroupGraphAsync → (
                       perms:   [(G1,"permB"), (G1,"permA"), (G2,"permC")],
                       parents: [(G1,G2)]
                  )
                → GroupClosure.Resolve({G1}, perms, parents) → {permA, permB, permC}
                → union with AccessRoles.Base = "heblo_user"
                → OrderBy ascending
                ↓
                Assert: Permissions.Equal(["heblo_user", "permA", "permB", "permC"])
```

### Test skeleton (illustrative — for the implementer)

```csharp
public class GetUserEffectivePermissionsHandlerTests
{
    [Fact]
    public async Task Handle_UserNotFound_ReturnsAuthorizationUserNotFound()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppUser?)null);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(new GetUserEffectivePermissionsRequest { UserId = userId }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph()
    {
        var userId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId, Email = "u@x.cz", DisplayName = "U", IsActive = false,
            UserGroups = new List<UserGroup> { new() { UserId = userId, GroupId = Guid.NewGuid() } }
        };
        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(new GetUserEffectivePermissionsRequest { UserId = userId }, default);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().BeEmpty();
        response.Permissions.Should().NotContain(AccessRoles.Base);
        repo.Verify(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveUser_ReturnsMergedSortedPermissionsIncludingBase()
    {
        var userId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var user = new AppUser
        {
            Id = userId, Email = "u@x.cz", DisplayName = "U", IsActive = true,
            UserGroups = new List<UserGroup> { new() { UserId = userId, GroupId = g1 } }
        };
        var perms = new List<GroupPermission>
        {
            new() { GroupId = g1, PermissionValue = "permB" },
            new() { GroupId = g1, PermissionValue = "permA" },
            new() { GroupId = g2, PermissionValue = "permC" }
        };
        var parents = new List<GroupParent> { new() { GroupId = g1, ParentGroupId = g2 } };

        var repo = new Mock<IAuthorizationRepository>();
        repo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        repo.Setup(r => r.GetGroupGraphAsync(It.IsAny<CancellationToken>())).ReturnsAsync((perms, parents));

        var handler = new GetUserEffectivePermissionsHandler(repo.Object);
        var response = await handler.Handle(new GetUserEffectivePermissionsRequest { UserId = userId }, default);

        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Permissions.Should().Equal(AccessRoles.Base, "permA", "permB", "permC");
    }
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| String ordering of `OrderBy(p => p)` is culture-aware, not ordinal. A future change to test inputs with non-ASCII or special characters could make `Should().Equal(...)` brittle. | Low | Test inputs are strict ASCII (`heblo_user`, `permA`/`B`/`C`). Document this in a one-line note inside the FR-4 test arrangement (or simply choose inputs that keep ordinal == culture). Do NOT change handler behavior. |
| `Times.Never` on `GetGroupGraphAsync` is the load-bearing assertion for the security guard. A test author could be tempted to drop it as "implementation detail". | High (security-regression) | Spec FR-3 explicitly calls this out as security-critical. The assertion is paired with the `NotContain(AccessRoles.Base)` check — both must remain. Note this in a brief inline comment ONLY if review otherwise misses it (per global no-comments rule, prefer a self-evident test name like `Handle_InactiveUser_ReturnsEmptyPermissionsAndDoesNotLoadGraph`). |
| `MockBehavior.Loose` means a misspelled setup silently returns a default — could mask a regression where `GetUserByIdAsync` is called with a different argument. | Low | Setup uses the exact `userId` (not `It.IsAny<Guid>()`), so a regression that changes the call signature would return `null` and FR-3 / FR-4 would fail visibly. |
| Test could fail under a future change to `GroupClosure.Resolve` ordering, even though that's not what we're testing. | Low | `GroupClosure.Resolve` returns a `HashSet<string>` per `GroupClosureTests`; the handler re-orders with `OrderBy` after merging. Our assertion is on the handler's final list, not on `Resolve`'s output, so this is robust. |
| The test does not pin down "request's `CancellationToken` is propagated to the repository". | Low | Not in scope per the spec. Sibling tests don't do this either. Skip. |

## Specification Amendments

1. **Minor clarification on ordering language (FR-4):** The spec says "ordered ascending by ordinal string comparison". The handler actually uses `OrderBy(p => p)`, which is **culture-aware** (`Comparer<string>.Default`), not ordinal. For the chosen ASCII test inputs the two orderings agree, so no test change is needed. Suggest updating the spec language to "ordered ascending using the default string comparer (culture-aware)" to avoid implying the handler does ordinal sorting. If anyone later wants ordinal stability in production, that's a separate change to the handler.

2. **FR-4 dedup edge case (already noted as optional):** Adding a fourth `[Fact]` for "AccessRoles.Base appears once even when a group also grants it" is genuinely cheap (~10 lines) and pins down the `HashSet` dedup behavior that FR-4 currently only covers implicitly. Recommend promoting this from "if needed" to a fourth `[Fact]` — keeps each fact single-purpose and avoids overloading the FR-4 test with two assertions.

3. **No change needed for `ErrorCode` field name:** The spec hedges with "use whatever the base `BaseResponse` exposes". Confirmed: it is `ErrorCode` (nullable `ErrorCodes?`), assertion is `response.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound)`. Spec can be tightened.

## Prerequisites

None. All required dependencies are already in place:

- `IAuthorizationRepository`, all four entity types, `GroupClosure`, `AccessRoles.Base`, `ErrorCodes.AuthorizationUserNotFound`, and `BaseResponse.{Success, ErrorCode}` exist on `main` (verified in source).
- `xUnit`, `Moq`, `FluentAssertions` are referenced by `Anela.Heblo.Tests` and used by every sibling handler test in the same folder.
- No migrations, no Key Vault entries, no DI registrations, no config flags.
- Validation steps from `CLAUDE.md`: `dotnet build` + `dotnet format` + `dotnet test --no-build` against the new file's project. No frontend impact.