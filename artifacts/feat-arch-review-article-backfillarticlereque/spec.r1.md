# Specification: Decouple Article Module from UserManagement's IGraphService

## Summary
The `BackfillArticleRequestedByHandler` in the Article module directly depends on `IGraphService`, a contract owned by the UserManagement module, violating cross-module communication rules. This feature introduces a consumer-owned contract (`IArticleUserResolver`) in the Article module and an adapter implementation in UserManagement, restoring the inverted-dependency pattern used elsewhere (e.g. `ILeafletKnowledgeSource`) and adding architectural test coverage to prevent regressions.

## Background
The codebase follows a Clean Architecture / Vertical Slice organization where modules communicate through **consumer-owned contracts**: the consuming module defines an interface in its own `Contracts/` folder, and the providing module implements an adapter. This keeps inter-module boundaries explicit and enforced.

The Article module's backfill admin command currently breaks this rule:

- File: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`
- Line 1: `using Anela.Heblo.Application.Features.UserManagement.Services;`
- Line 16: Constructor injects `IGraphService _graph`
- Line 37: Calls `_graph.GetGroupMembersAsync(request.GroupId, ct)`

`IGraphService` is owned and registered by the UserManagement module. This creates three problems:

1. **Compile-time coupling** — A rename or signature change to `IGraphService` in UserManagement breaks the Article module's build.
2. **Hidden DI dependency** — The Article handler silently relies on `AddUserManagementModule()` running before the Article backfill command can resolve.
3. **Undetected boundary violation** — `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already enforces `Article → KnowledgeBase` but has no `Article → UserManagement` rule, so this regression slipped past CI and any future regressions in the same direction would as well.

The remediation aligns with existing precedent: `ILeafletKnowledgeSource` is the contract used by KnowledgeBase consumers — Article defines its needs, and the providing module supplies the implementation.

## Functional Requirements

### FR-1: Define consumer-owned contract in Article module
Create a new contract interface in the Article module's `Contracts/` folder describing the user-resolution capability the backfill handler actually needs — group-id-to-user-list resolution, expressed in Article's own vocabulary, with no Graph or Azure AD terminology.

**Files to create:**
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs`

**Interface shape:**
```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public interface IArticleUserResolver
{
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct);
}

public sealed record ArticleUserMatch(string Id, string DisplayName);
```

**Acceptance criteria:**
- New file exists at the specified path.
- Interface and record are declared in the `Anela.Heblo.Application.Features.Article.Contracts` namespace.
- Interface depends only on BCL types and types in the Article module.
- No `using` directive references `UserManagement`.
- `ArticleUserMatch` is an internal-domain record (records are allowed for internal types per `development_guidelines.md`; it is not a DTO exposed via OpenAPI).

### FR-2: Refactor BackfillArticleRequestedByHandler to consume the new contract
Replace the direct `IGraphService` dependency with `IArticleUserResolver`. The handler logic must remain functionally identical: same backfill behavior, same outputs, same error handling.

**Files to modify:**
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`

**Changes:**
- Remove `using Anela.Heblo.Application.Features.UserManagement.Services;`
- Add `using Anela.Heblo.Application.Features.Article.Contracts;`
- Change the injected field type from `IGraphService` to `IArticleUserResolver` (and rename the field accordingly, e.g. `_userResolver`).
- Replace `_graph.GetGroupMembersAsync(request.GroupId, ct)` with `_userResolver.ResolveByGroupAsync(request.GroupId, ct)`.
- Adapt downstream code that consumed the previous result type (Graph group-member objects) to consume `ArticleUserMatch` (`Id`, `DisplayName`).

**Acceptance criteria:**
- The handler file contains no reference to `UserManagement`, `IGraphService`, or any Graph types.
- All behavior covered by existing tests for the backfill command continues to pass.
- `dotnet build` succeeds.
- `dotnet format` reports no changes needed.

### FR-3: Provide adapter implementation in UserManagement module
Create an adapter in the UserManagement module that implements `IArticleUserResolver` by delegating to `IGraphService`. The adapter is responsible for mapping Graph-shaped results to `ArticleUserMatch` instances.

**Files to create:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`

**Implementation shape:**
```csharp
namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

public sealed class GraphArticleUserResolver : IArticleUserResolver
{
    private readonly IGraphService _graph;

    public GraphArticleUserResolver(IGraphService graph) => _graph = graph;

    public async Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct)
    {
        var members = await _graph.GetGroupMembersAsync(groupId, ct);
        return members
            .Select(m => new ArticleUserMatch(m.Id, m.DisplayName))
            .ToList();
    }
}
```

**Acceptance criteria:**
- File exists at the specified path inside the UserManagement module.
- Class is `sealed` and depends on `IGraphService` via constructor injection.
- Null/empty-member edge cases produce an empty list rather than throwing.
- Mapping preserves `Id` and `DisplayName` from each Graph member with no transformation.

### FR-4: Register the adapter in the UserManagement module bootstrap
Wire `IArticleUserResolver → GraphArticleUserResolver` in UserManagement's DI registration so consumers (Article) can resolve it transparently.

**Files to modify:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` (or whichever file contains the module's `AddUserManagementModule` extension; locate the existing registration of `IGraphService` and add the new line adjacent to it)

**Change:**
```csharp
services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
```

**Acceptance criteria:**
- The registration is added to the existing `AddUserManagementModule` (or equivalent) method.
- DI lifetime matches `IGraphService`'s lifetime (assume `Scoped`; verify against actual registration).
- The application boots successfully; `IArticleUserResolver` can be resolved at runtime.
- No registration of `IArticleUserResolver` exists in the Article module's DI bootstrap.

### FR-5: Add architectural test enforcing Article → UserManagement boundary
Extend `ModuleBoundariesTests.cs` to assert that the Article module's source code has no dependency on `UserManagement` types. This must fail loudly if a future change reintroduces the violation.

**Files to modify:**
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Test shape:**
Add a new test (following the existing `Article → KnowledgeBase` rule as a template) that asserts no type under `Anela.Heblo.Application.Features.Article` depends on any type under `Anela.Heblo.Application.Features.UserManagement`. No allowlist exceptions should be necessary after the refactor.

**Acceptance criteria:**
- The new test is named to match the pattern of existing rules (e.g. `Article_ShouldNotDependOn_UserManagement`).
- Running the test against the refactored code passes with no allowlist entries.
- Manually re-introducing a `UserManagement` `using` in the Article module causes the test to fail.

### FR-6: Verify nothing else in Article references UserManagement
Confirm `BackfillArticleRequestedByHandler` is the only offender. If other files in `Features/Article/` import from `Features/UserManagement/`, either remediate them with the same pattern or document them as out-of-scope follow-ups.

**Acceptance criteria:**
- Grep results for `using Anela.Heblo.Application.Features.UserManagement` under `backend/src/Anela.Heblo.Application/Features/Article/` return zero matches after the refactor.
- If additional references are found, they are either fixed in this change or filed as a follow-up issue (noted in Open Questions).

## Non-Functional Requirements

### NFR-1: Performance
No measurable runtime overhead. The adapter adds a single in-process method indirection and a `Select(...).ToList()` projection over a small, bounded collection (group members). Backfill is an admin-triggered command, not a hot path; performance is not a constraint.

### NFR-2: Security
No change to the security posture. The adapter does not log, persist, or transmit member data beyond what the current handler already does. Authorization on the backfill command (whatever guards the existing endpoint) is preserved.

### NFR-3: Architectural conformance
After this change, the Article module's compile-time dependency graph must not include the UserManagement module. This is enforced by FR-5.

### NFR-4: Backwards compatibility
Public API behavior of the backfill admin endpoint is unchanged — same request shape, same response shape, same status codes, same error semantics. No database schema changes, no migrations, no client regeneration required.

### NFR-5: Testability
The Article module's handler tests can now mock `IArticleUserResolver` directly without dragging in any Graph types or UserManagement dependencies. Existing handler tests (if they mocked `IGraphService`) must be updated to mock `IArticleUserResolver` instead.

## Data Model

No persistent data model changes. One in-memory type is introduced:

| Type | Module | Kind | Purpose |
|------|--------|------|---------|
| `IArticleUserResolver` | Article | Interface | Consumer-owned contract; abstracts group-to-user resolution from Article's perspective |
| `ArticleUserMatch` | Article | Record | DTO-internal projection of a user (Id + DisplayName) for the backfill use case |
| `GraphArticleUserResolver` | UserManagement | Class | Adapter; implements `IArticleUserResolver` by delegating to `IGraphService` |

`ArticleUserMatch` is internal to the application layer and not serialized over the wire, so a `record` is appropriate (the DTO-must-be-class rule applies only to OpenAPI-exposed contracts).

## API / Interface Design

No HTTP, MediatR, or external API surface changes.

**Internal interface added:**
```csharp
public interface IArticleUserResolver
{
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct);
}
public sealed record ArticleUserMatch(string Id, string DisplayName);
```

**DI binding added (UserManagement):**
```csharp
services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
```

The MediatR command (`BackfillArticleRequestedByCommand` or equivalent) and its HTTP entry point keep their existing signatures and behavior.

## Dependencies

- **UserManagement module** must continue to register `IGraphService` (no change). The adapter depends on this existing registration.
- **Architecture test framework** already present in `backend/test/Anela.Heblo.Tests/Architecture/` (NetArchTest or similar — follow the pattern already used for the `Article → KnowledgeBase` rule).
- **Module load order** — `AddUserManagementModule` must continue to run as part of the application bootstrap so the `IArticleUserResolver` binding is registered. This is the existing behavior; no change required.

## Out of Scope

- Other architecture violations not flagged in the brief (Article-internal or cross-module dependencies beyond the specific `IGraphService` import in `BackfillArticleRequestedByHandler`).
- Refactoring `IGraphService` itself or its callers within the UserManagement module.
- Introducing a generic user-resolution abstraction shared across modules. Each consumer should own its own contract; cross-module reuse of `IArticleUserResolver` is explicitly rejected — if another module needs similar functionality, it defines its own contract and its own adapter.
- Performance tuning, caching, or batching of group-member queries.
- Adding new architecture test rules beyond `Article → UserManagement` (other module pairs are out of scope for this change).
- Renaming or restructuring the `BackfillArticleRequestedByHandler` itself or the associated MediatR command.
- Changes to the admin UI or any frontend code (this is a backend-only refactor).

## Open Questions

None.

## Status: COMPLETE