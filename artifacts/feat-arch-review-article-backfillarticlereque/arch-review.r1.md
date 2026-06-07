I have enough context to write the review. The proposed approach mirrors the established `IArticleStyleGuideSource` / `KnowledgeBaseArticleStyleGuideSource` precedent in this very codebase, so my role is mainly to confirm fit and flag a few concrete corrections.

# Architecture Review: Decouple Article Module from UserManagement's IGraphService

## Skip Design: true

Backend-only refactor — no UI components, screens, or visual changes. The admin endpoint shape is unchanged.

## Architectural Fit Assessment

The proposed design **fully aligns** with established codebase precedent. The Article module already owns one cross-module contract using exactly this pattern:

- **Precedent A** — `IArticleStyleGuideSource` (Article module, `Features/Article/Contracts/`) is implemented by `KnowledgeBaseArticleStyleGuideSource` in `Features/KnowledgeBase/Infrastructure/`, registered in `KnowledgeBaseModule`.
- **Precedent B** — `ILeafletKnowledgeSource` (Leaflet module) is implemented by `KnowledgeBaseLeafletSourceAdapter` (`internal sealed`) in KnowledgeBase, registered in `KnowledgeBaseModule`.

The new `IArticleUserResolver` ↔ `GraphArticleUserResolver` pair is structurally identical to those, so there is no architectural friction. The only integration surfaces are:

1. **DI bootstrap** — one line added next to the existing `IGraphService` registration in `UserManagementModule.AddUserManagement()`.
2. **Compile-time graph** — Article's `Features/Article/Admin/` namespace loses its `using Anela.Heblo.Application.Features.UserManagement.Services;` import.
3. **Architecture test** — new entry in `ModuleBoundariesTests.Rules()`.

No new namespaces, no infrastructure, no migrations, no client regeneration.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────┐         ┌──────────────────────────────────────────────────┐
│  Article module                             │         │  UserManagement module                           │
│                                             │         │                                                  │
│  Features/Article/Admin/                    │         │  Features/UserManagement/Infrastructure/         │
│    BackfillArticleRequestedByHandler  ────┐ │  uses   │    GraphArticleUserResolver  ────┐               │
│      (depends on IArticleUserResolver)    │ ├────────►│      (impl IArticleUserResolver) │               │
│                                           │ │         │                                  │ delegates to  │
│  Features/Article/Contracts/              │ │         │                                  ▼               │
│    IArticleUserResolver       ◄───────────┘ │ impl    │  Features/UserManagement/Services/               │
│    ArticleUserMatch (record)        ◄───────┼─────────│    IGraphService / GraphService / MockGraphSvc   │
│                                             │         │                                                  │
└─────────────────────────────────────────────┘         └──────────────────────────────────────────────────┘
        (consumer)                                                    (provider + adapter)

Compile-time direction:  UserManagement.Infrastructure → Article.Contracts   (provider depends on consumer)
DI wiring:               UserManagementModule registers IArticleUserResolver → GraphArticleUserResolver
```

### Key Design Decisions

#### Decision 1: Adapter visibility — `internal sealed`, not `public sealed`

**Options considered:** `public sealed` (as written in spec) vs `internal sealed` (matches `KnowledgeBaseLeafletSourceAdapter`).

**Chosen approach:** `internal sealed`.

**Rationale:** The adapter is a DI-registered implementation detail of the UserManagement module. Only the DI container needs to instantiate it; no external code should reference the concrete type. `KnowledgeBaseLeafletSourceAdapter` uses `internal sealed` for exactly this reason. DI works fine with internal classes within the same assembly (`Anela.Heblo.Application`). Use `internal sealed` to match precedent and prevent accidental coupling to the concrete class.

#### Decision 2: Adapter location — `Features/UserManagement/Infrastructure/`

**Options considered:** `Features/UserManagement/Infrastructure/`, `Features/UserManagement/Services/`, or a new `Features/UserManagement/Adapters/`.

**Chosen approach:** `Features/UserManagement/Infrastructure/`.

**Rationale:** This is a new folder for UserManagement (the module currently has `Contracts/`, `Services/`, `UseCases/`), but it matches the existing convention in `Features/KnowledgeBase/Infrastructure/` and `Features/Leaflet/Infrastructure/` for adapters and integration code. `Services/` is reserved for primary in-module services like `IGraphService`. Do not create `Adapters/` — that would be a new convention without precedent.

#### Decision 3: Contract return type — `IReadOnlyList<ArticleUserMatch>`

**Options considered:** `List<T>` (brief), `IReadOnlyList<T>` (spec), `IEnumerable<T>`.

**Chosen approach:** `IReadOnlyList<ArticleUserMatch>` (spec is correct, brief was loose).

**Rationale:** The handler enumerates and groups the result; it does not mutate the list. Returning `IReadOnlyList<T>` signals immutability to consumers, matches modern .NET style, and aligns with the `ILeafletKnowledgeSource.SearchSimilarAsync` return type. The adapter still materializes a `List<T>` internally and exposes it as `IReadOnlyList<T>`.

#### Decision 4: Record vs class for `ArticleUserMatch`

**Options considered:** `record` (spec) vs `class` (project rule: "DTOs are classes, never C# records").

**Chosen approach:** `record`.

**Rationale:** The DTOs-are-classes rule applies **only to OpenAPI-exposed contracts** because the TypeScript generator mishandles record parameter order. `ArticleUserMatch` is purely internal to the application layer — it never crosses the HTTP boundary, never appears in `Anela.Heblo.Application.Shared` or controller signatures. Per `docs/architecture/development_guidelines.md`, records are allowed for internal domain/application types. Spec correctly identifies this.

## Implementation Guidance

### Directory / Module Structure

**Create:**
```
backend/src/Anela.Heblo.Application/Features/Article/Contracts/
    IArticleUserResolver.cs             # contract + ArticleUserMatch record (single file, follows IArticleStyleGuideSource shape)

backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/
    GraphArticleUserResolver.cs         # internal sealed adapter
```

**Modify:**
```
backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs
    - drop UserManagement using
    - swap IGraphService → IArticleUserResolver, field _graph → _userResolver
    - swap GetGroupMembersAsync → ResolveByGroupAsync
    - downstream consumers of UserDto.Id/DisplayName work unchanged against ArticleUserMatch.Id/DisplayName

backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs
    - in both branches of the if/else (mock vs real), add adjacent to IGraphService registration:
        services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();
      The adapter delegates to IGraphService, so it works the same way whether IGraphService is Mock or real.
      A single registration outside the if/else is preferred — see spec amendment below.

backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs
    - drop `using Anela.Heblo.Application.Features.UserManagement.*`
    - replace Mock<IGraphService> with Mock<IArticleUserResolver>
    - replace `Member(...)` helper to construct ArticleUserMatch instead of UserDto
    - all existing assertions remain valid

backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
    - add new ModuleBoundaryRule for Article → UserManagement (see Interfaces section below)
```

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction for resolving the set of users associated
/// with a directory group (used by the RequestedBy backfill admin command).
/// Implemented by the UserManagement module via an adapter.
/// </summary>
public interface IArticleUserResolver
{
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken);
}

public sealed record ArticleUserMatch(string Id, string DisplayName);
```

```csharp
// Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs
namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class GraphArticleUserResolver : IArticleUserResolver
{
    private readonly IGraphService _graph;

    public GraphArticleUserResolver(IGraphService graph) => _graph = graph;

    public async Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken)
    {
        var members = await _graph.GetGroupMembersAsync(groupId, cancellationToken);
        return members
            .Select(m => new ArticleUserMatch(m.Id, m.DisplayName))
            .ToList();
    }
}
```

```csharp
// Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs — new rule (no allowlist entries needed)
new ModuleBoundaryRule(
    Name: "Article -> UserManagement",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Application.Features.UserManagement",
        // Domain/Persistence subnamespaces for UserManagement currently don't exist,
        // but include the prefixes defensively to catch future additions.
        "Anela.Heblo.Domain.Features.UserManagement",
        "Anela.Heblo.Persistence.UserManagement",
    },
    Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

### Data Flow

```
Backfill admin command (HTTP)
        │
        ▼
BackfillArticleRequestedByHandler.Handle(GroupId, DryRun)
        │
        │  _userResolver.ResolveByGroupAsync(GroupId, ct)
        ▼
[DI resolves IArticleUserResolver → GraphArticleUserResolver]
        │
        │  _graph.GetGroupMembersAsync(GroupId, ct)
        ▼
GraphService / MockGraphService → List<UserDto>
        │
        │  .Select(m => new ArticleUserMatch(m.Id, m.DisplayName)).ToList()
        ▼
IReadOnlyList<ArticleUserMatch>
        │
        ▼
Handler groups by DisplayName (OrdinalIgnoreCase), iterates rows from IArticleAdminRepository,
applies LooksLikeIdentifier / unique-match / ambiguous / unresolved logic,
saves repository changes when DryRun is false and any row resolved.
```

The handler's existing logic (`LooksLikeIdentifier`, the GroupBy on `DisplayName`, the resolution counters, the dry-run guard, the SaveChanges decision) is **completely unaffected**. The only change is the type of `members` and the field name.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec uses wrong DI extension method name (`AddUserManagementModule`); actual is `AddUserManagement`. | Low | Implementer locates by file (`UserManagementModule.cs`) and finds existing `IGraphService` registration; this review records the correction. |
| `UserManagementModule.AddUserManagement` registers `IGraphService` inside an if/else (mock vs real). Naively adding the adapter line into only one branch would break the other auth mode. | Medium | Register `IArticleUserResolver → GraphArticleUserResolver` **once, outside the if/else** (the adapter depends on `IGraphService`, whichever implementation DI resolves at runtime). See Spec Amendment 1. |
| `BackfillArticleRequestedByHandlerTests.cs` is not listed in spec FR-2 file changes, but it depends on `IGraphService` and `UserDto` and will fail to compile after the refactor. | Medium | Add explicit test update to the implementation checklist. Spec NFR-5 mentions this conceptually; the file path needs to be called out. See Spec Amendment 2. |
| Future developer reintroduces `using ...UserManagement` in another Article file (e.g., `UseCases/...`). | Medium | New `ModuleBoundariesTests` rule (FR-5) catches this at CI time. Mitigation is the feature itself. |
| Adapter exposed as `public sealed` (per spec) invites external callers to reference the concrete type and bypass the interface. | Low | Make adapter `internal sealed` to match `KnowledgeBaseLeafletSourceAdapter`. See Spec Amendment 3. |
| The new architecture rule's forbidden-namespace list omits `Anela.Heblo.Domain.Features.UserManagement` / `Anela.Heblo.Persistence.UserManagement`. Those subnamespaces don't exist today but could be added later, silently allowing a new violation channel. | Low | Include all three prefixes in the forbidden list for defensive symmetry with the other rules (Logistics → Manufacture, etc.). See Spec Amendment 4. |
| Architectural test scans only the `Anela.Heblo.Application` assembly. If UserManagement domain types are later added under `Anela.Heblo.Domain.Features.UserManagement` and the Article handler picks them up, the rule must already cover Domain. | Low | Same mitigation as above. The forbidden-prefix list covers Domain even before the namespace exists. |

## Specification Amendments

The spec is largely correct; the following targeted corrections must be applied during implementation:

### Amendment 1 — Correct method name and registration placement in `UserManagementModule`

FR-4 refers to `AddUserManagementModule`. The actual extension method is `AddUserManagement` in `UserManagementModule.cs`. Moreover, the method registers `IGraphService` inside an `if (useMockAuth || bypassJwtValidation)` / `else` block — both branches need `IArticleUserResolver` available, so the new registration must be placed **outside the if/else** (it works identically whether `IGraphService` resolves to `MockGraphService` or `GraphService`):

```csharp
public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
{
    var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
    var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

    if (useMockAuth || bypassJwtValidation)
    {
        services.AddScoped<IGraphService, MockGraphService>();
    }
    else
    {
        services.AddHttpClient("MicrosoftGraph");
        services.AddScoped<IGraphService, GraphService>();
    }

    // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
    services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

    return services;
}
```

### Amendment 2 — Add explicit FR for test-file refactor

NFR-5 mentions this in passing but does not enumerate the file. Promote it to an FR (call it FR-7 or fold into FR-2's "Files to modify"):

> **FR-7: Update `BackfillArticleRequestedByHandlerTests.cs`**
> - Path: `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs`
> - Remove `using Anela.Heblo.Application.Features.UserManagement.Contracts;` and `using Anela.Heblo.Application.Features.UserManagement.Services;`
> - Replace `Mock<IGraphService> _graph` with `Mock<IArticleUserResolver> _userResolver`
> - Replace the `Member(id, displayName)` helper to return `ArticleUserMatch` instead of `UserDto`
> - Replace all `_graph.Setup(g => g.GetGroupMembersAsync(...))` calls with `_userResolver.Setup(r => r.ResolveByGroupAsync(...))`
> - Acceptance: all existing test cases pass without behavioral changes (test bodies remain otherwise unmodified).

### Amendment 3 — Adapter is `internal sealed`, not `public sealed`

FR-3 specifies `public sealed class GraphArticleUserResolver`. Change to `internal sealed class` to match the precedent `KnowledgeBaseLeafletSourceAdapter`. DI works identically; the adapter has no callers outside the DI container.

### Amendment 4 — Architectural test forbidden-prefix list

FR-5 should explicitly list all three sibling namespace prefixes (Application, Domain, Persistence) for UserManagement, matching the shape of existing rules in `ModuleBoundariesTests.cs` (e.g., Logistics → Manufacture). Domain and Persistence variants do not exist today but the rule must be forward-defensive:

```csharp
ForbiddenNamespacePrefixes: new[]
{
    "Anela.Heblo.Application.Features.UserManagement",
    "Anela.Heblo.Domain.Features.UserManagement",
    "Anela.Heblo.Persistence.UserManagement",
},
```

### Amendment 5 — XML doc on the contract

Add an XML `<summary>` to `IArticleUserResolver` matching the precedent shape used by `IArticleStyleGuideSource` and `ILeafletKnowledgeSource`. This is not behavioral but it documents the consumer-owned-contract intent at the type level.

## Prerequisites

None. All required infrastructure already exists:

- `Anela.Heblo.Application` assembly already contains the `Features/Article/Contracts/` folder (with `IArticleStyleGuideSource.cs`).
- `Anela.Heblo.Application` assembly does not yet contain `Features/UserManagement/Infrastructure/`, but creating a new folder is a no-op (no MSBuild changes; the project SDK auto-includes `.cs` files).
- `ModuleBoundariesTests` already supports an extensible rules table — the new rule is one entry in `Rules()`.
- `IGraphService` continues to be registered by `AddUserManagement`; the new adapter depends on it directly.
- xUnit + Moq + FluentAssertions are already in `Anela.Heblo.Tests`; no new test dependencies.
- No database migrations, no Azure Key Vault secrets, no config changes, no OpenAPI client regeneration.

Implementation can begin immediately.