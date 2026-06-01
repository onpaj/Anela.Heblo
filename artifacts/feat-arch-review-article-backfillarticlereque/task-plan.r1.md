# Decouple Article Module from UserManagement's IGraphService — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the Article module's direct dependency on UserManagement's `IGraphService` by introducing a consumer-owned contract (`IArticleUserResolver`) in the Article module, an adapter (`GraphArticleUserResolver`) in UserManagement that implements it via `IGraphService`, and an architecture test that prevents future Article → UserManagement boundary violations.

**Architecture:** Standard consumer-owned contract pattern already used in this codebase (mirrors `IArticleStyleGuideSource` ↔ `KnowledgeBaseArticleStyleGuideSource` and `ILeafletKnowledgeSource` ↔ `KnowledgeBaseLeafletSourceAdapter`). The Article module declares an interface in its `Contracts/` folder; UserManagement provides an `internal sealed` adapter under its `Infrastructure/` folder and registers it in `UserManagementModule.AddUserManagement`. DI binding is added **once outside the existing mock/real `IGraphService` if/else** so the adapter resolves regardless of auth mode.

**Tech Stack:** .NET 8, C# nullable reference types, MediatR, xUnit + Moq + FluentAssertions, NetArchTest-style reflection-based boundary tests (custom helper in `ModuleBoundariesTests.cs`).

---

## File Structure

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs` — interface + `ArticleUserMatch` record. Single file matching the shape of `IArticleStyleGuideSource.cs`. Article-owned vocabulary; depends only on BCL.
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — `internal sealed` adapter delegating to `IGraphService` and mapping `UserDto` → `ArticleUserMatch`.

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — drop UserManagement `using`; swap `IGraphService _graph` for `IArticleUserResolver _userResolver`; swap `GetGroupMembersAsync` for `ResolveByGroupAsync`. Logic unchanged.
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — add one DI registration outside the mock-vs-real `if/else`.
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — swap `Mock<IGraphService>` → `Mock<IArticleUserResolver>`; rewrite `Member` helper to construct `ArticleUserMatch`; rewrite mock setups accordingly. All test bodies otherwise unchanged.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add `Article -> UserManagement` rule to the `Rules()` `TheoryData`. No allowlist entries.

**No changes to:** controllers, routing, OpenAPI specs, database schema, frontend code, Azure config, deployment.

---

## Task 1: Add the failing architecture-boundary test (RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (add new `ModuleBoundaryRule` entry in `Rules()`)

- [ ] **Step 1: Add the new rule to `Rules()`**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`. Locate the `public static TheoryData<ModuleBoundaryRule> Rules() => new()` block (around line 85). Insert the following `new ModuleBoundaryRule(...)` entry immediately after the existing `Article -> KnowledgeBase` rule (it ends around line 107) — keep `Article` rules grouped together. The forbidden-prefix list defensively covers Domain and Persistence subnamespaces even though they don't exist for UserManagement today.

```csharp
        new ModuleBoundaryRule(
            Name: "Article -> UserManagement",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.UserManagement",
                "Anela.Heblo.Application.Features.UserManagement",
                "Anela.Heblo.Persistence.UserManagement",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),
```

- [ ] **Step 2: Run the new rule to verify it fails (RED)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```

Expected: the theory case for `Article -> UserManagement` **FAILS** with a violation message naming `Anela.Heblo.Application.Features.Article.Admin.BackfillArticleRequestedByHandler -> Anela.Heblo.Application.Features.UserManagement.Services.IGraphService` (and possibly other UserManagement types referenced by the handler — that's fine).

Do **not** commit yet — the codebase is intentionally in a RED state. The next tasks implement the refactor to turn it GREEN.

---

## Task 2: Create the consumer-owned contract `IArticleUserResolver`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs`

- [ ] **Step 1: Create the contract file**

Create `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs` with the following content. XML doc matches the style used by `IArticleStyleGuideSource`. `ArticleUserMatch` is an internal projection (records are permitted because it never crosses the OpenAPI boundary).

```csharp
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

- [ ] **Step 2: Verify the contract compiles in isolation**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build **succeeds**. The new file has no consumers yet, so adding it is a non-breaking change.

---

## Task 3: Create the adapter `GraphArticleUserResolver` in UserManagement

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`

- [ ] **Step 1: Create the adapter file**

Create `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`. The class is `internal sealed` (matches `KnowledgeBaseLeafletSourceAdapter` precedent — only DI needs to instantiate it; nothing outside this assembly should reference the concrete type). The folder `Features/UserManagement/Infrastructure/` does not yet exist; creating a `.cs` file under it auto-includes the folder via the .NET SDK — no project-file edits required.

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class GraphArticleUserResolver : IArticleUserResolver
{
    private readonly IGraphService _graph;

    public GraphArticleUserResolver(IGraphService graph)
    {
        _graph = graph;
    }

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

- [ ] **Step 2: Verify the adapter compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build **succeeds**. The adapter has no callers yet other than the DI container (registration comes in Task 4), so adding it is non-breaking.

---

## Task 4: Register the adapter in `UserManagementModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`

- [ ] **Step 1: Add the using directive and DI registration**

Open `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs`. The current file structure has a single `AddUserManagement` extension method with an `if (useMockAuth || bypassJwtValidation) { ... } else { ... }` that registers `IGraphService` differently per branch. The adapter depends on `IGraphService` — whichever implementation DI resolves at runtime — so the registration must sit **outside the if/else**.

Add the using directive at the top (alphabetical order with existing usings):

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Infrastructure;
```

(The first line — `Anela.Heblo.Application.Features.Article.Contracts` — is required for the `IArticleUserResolver` symbol. The second — `Anela.Heblo.Application.Features.UserManagement.Infrastructure` — is required for the `GraphArticleUserResolver` symbol.)

Then update the body of `AddUserManagement` to add the new registration after the `if/else`, immediately before `return services;`. The full method body should look like this after the change:

```csharp
    public static IServiceCollection AddUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        // Check if mock authentication is enabled
        var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        if (useMockAuth || bypassJwtValidation)
        {
            // Register mock GraphService for mock authentication
            services.AddScoped<IGraphService, MockGraphService>();
        }
        else
        {
            // Register the named "MicrosoftGraph" HttpClient for IHttpClientFactory.
            // Matches the shared "MicrosoftGraph" named client used by Marketing/MeetingTasks/CatalogDocuments/KnowledgeBase/Photobank modules.
            services.AddHttpClient("MicrosoftGraph");

            // Register real GraphService for production authentication
            services.AddScoped<IGraphService, GraphService>();

            // Note: GraphServiceClient must be registered in the API layer with proper authentication
            // through Microsoft.Identity.Web's AddMicrosoftGraph() method
        }

        // Cross-module contract: UserManagement implements Article's IArticleUserResolver via adapter.
        // Works regardless of which IGraphService implementation (Mock vs real) is registered above.
        services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();

        // Note: HttpContextAccessor must be registered in the API layer

        return services;
    }
```

- [ ] **Step 2: Verify the application still builds**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build **succeeds**. `IArticleUserResolver` is now registered but still unused by any handler, so behavior is unchanged.

---

## Task 5: Refactor the handler and its tests in lockstep

The handler and its tests must change together because the test file currently mocks `IGraphService` to inject into the handler's constructor. Refactoring just one side breaks the build.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs`

- [ ] **Step 1: Refactor `BackfillArticleRequestedByHandler.cs`**

Replace the **entire file contents** with the version below. The behavioral logic (`LooksLikeIdentifier`, `GroupBy` on display name, dry-run guard, save-changes condition, all logging) is identical — only the dependency type, field name, and one method call change.

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByHandler
    : IRequestHandler<BackfillArticleRequestedByCommand, BackfillArticleRequestedByResponse>
{
    private readonly IArticleAdminRepository _repository;
    private readonly IArticleUserResolver _userResolver;
    private readonly ILogger<BackfillArticleRequestedByHandler> _logger;

    public BackfillArticleRequestedByHandler(
        IArticleAdminRepository repository,
        IArticleUserResolver userResolver,
        ILogger<BackfillArticleRequestedByHandler> logger)
    {
        _repository = repository;
        _userResolver = userResolver;
        _logger = logger;
    }

    public async Task<BackfillArticleRequestedByResponse> Handle(
        BackfillArticleRequestedByCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GroupId))
        {
            return new BackfillArticleRequestedByResponse(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "field", "GroupId" } });
        }

        var members = await _userResolver.ResolveByGroupAsync(request.GroupId, ct);
        var byDisplayName = members
            .GroupBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = await _repository.ListWithRequestedByAsync(ct);
        var response = new BackfillArticleRequestedByResponse
        {
            Total = rows.Count,
            WasDryRun = request.DryRun,
        };

        var anyResolved = false;

        foreach (var row in rows)
        {
            var original = row.RequestedBy!;

            if (LooksLikeIdentifier(original))
            {
                response.AlreadyMigrated++;
                _logger.LogInformation(
                    "Article {ArticleId} RequestedBy={Value} already looks like an identifier; skipping.",
                    row.Id, original);
                continue;
            }

            if (!byDisplayName.TryGetValue(original, out var matches))
            {
                response.Unresolved++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = "no match in Graph group members",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} has no match in group {GroupId}.",
                    row.Id, original, request.GroupId);
                continue;
            }

            if (matches.Count > 1)
            {
                response.Ambiguous++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = $"ambiguous: {matches.Count} group members share this display name",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} is ambiguous ({Count} matches).",
                    row.Id, original, matches.Count);
                continue;
            }

            var match = matches[0];
            if (!request.DryRun)
            {
                row.RequestedBy = match.Id;
            }
            anyResolved = true;
            response.Resolved++;
            _logger.LogInformation(
                "Article {ArticleId} resolved: {DisplayName} -> {Id}.",
                row.Id, original, match.Id);
        }

        if (anyResolved && !request.DryRun)
        {
            await _repository.SaveChangesAsync(ct);
        }

        return response;
    }

    private static bool LooksLikeIdentifier(string value)
    {
        if (Guid.TryParse(value, out _))
        {
            return true;
        }

        return value.Contains('@', StringComparison.Ordinal);
    }
}
```

Confirm the file:
- Contains **no** `using Anela.Heblo.Application.Features.UserManagement...` directive.
- Field is `_userResolver` of type `IArticleUserResolver`.
- The only behavioral change is `_graph.GetGroupMembersAsync(...)` → `_userResolver.ResolveByGroupAsync(...)`.
- Log messages and the `"no match in Graph group members"` reason string are unchanged (the existing test asserts `Reason.Contains("no match")` so this is safe; we deliberately keep the string identical to avoid spec drift and to keep existing test assertions valid).

- [ ] **Step 2: Refactor `BackfillArticleRequestedByHandlerTests.cs`**

Replace the **entire file contents** with the version below. The changes:
- Drop `using Anela.Heblo.Application.Features.UserManagement.Contracts;` and `using Anela.Heblo.Application.Features.UserManagement.Services;`.
- Add `using Anela.Heblo.Application.Features.Article.Contracts;`.
- Field `Mock<IGraphService> _graph` → `Mock<IArticleUserResolver> _userResolver`.
- `CreateHandler()` passes `_userResolver.Object` in place of `_graph.Object`.
- `Member` helper returns `ArticleUserMatch` (positional ctor) instead of `UserDto`.
- All `_graph.Setup(g => g.GetGroupMembersAsync(...))` calls become `_userResolver.Setup(r => r.ResolveByGroupAsync(...))`.
- Mock return-type lists become `List<ArticleUserMatch>` (still satisfies `IReadOnlyList<ArticleUserMatch>` since `List<T>` implements `IReadOnlyList<T>`).
- All assertions and test names remain identical.

```csharp
using Anela.Heblo.Application.Features.Article.Admin;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Admin;

public class BackfillArticleRequestedByHandlerTests
{
    private const string GroupId = "marketing-group-id";

    private readonly Mock<IArticleAdminRepository> _repository = new();
    private readonly Mock<IArticleUserResolver> _userResolver = new();

    private BackfillArticleRequestedByHandler CreateHandler() =>
        new(_repository.Object, _userResolver.Object, NullLogger<BackfillArticleRequestedByHandler>.Instance);

    private static DomainArticle Row(string requestedBy)
        => new() { Id = Guid.NewGuid(), Topic = "Topic", RequestedBy = requestedBy };

    private static ArticleUserMatch Member(string id, string displayName)
        => new(id, displayName);

    [Fact]
    public async Task Handle_MissingGroupId_ReturnsValidationError()
    {
        var request = new BackfillArticleRequestedByCommand { GroupId = "", DryRun = true };

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_SkipsGuidShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row(Guid.NewGuid().ToString());
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().BeEmpty();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsEmailShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row("john@example.com");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UniqueDisplayNameMatch_ResolvesRow()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeFalse();
        row.RequestedBy.Should().Be("jan-oid");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AmbiguousDisplayName_LeavesRowAndReports()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch>
            {
                Member("jan-oid-a", "Jan Novák"),
                Member("jan-oid-b", "Jan Novák"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Ambiguous.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.ArticleId == row.Id && u.OriginalValue == "Jan Novák" && u.Reason.Contains("ambiguous"));
        row.RequestedBy.Should().Be("Jan Novák");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownDisplayName_LeavesRowAndReports()
    {
        var row = Row("Ghost User");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch> { Member("someone-oid", "Someone Else") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.OriginalValue == "Ghost User" && u.Reason.Contains("no match"));
        row.RequestedBy.Should().Be("Ghost User");
    }

    [Fact]
    public async Task Handle_DryRun_DoesNotSaveResolvedRows()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeTrue();
        // Dry-run must not mutate entities — entity state unchanged
        row.RequestedBy.Should().Be("Jan Novák");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MixedSet_CountsCorrectly()
    {
        var rows = new List<DomainArticle>
        {
            Row(Guid.NewGuid().ToString()),    // already migrated (GUID)
            Row("ondra@example.com"),           // already migrated (email)
            Row("Jan Novák"),                  // resolved
            Row("Petra Dvořáková"),            // ambiguous
            Row("Ghost User"),                  // unresolved
        };
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _userResolver.Setup(r => r.ResolveByGroupAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleUserMatch>
            {
                Member("jan-oid", "Jan Novák"),
                Member("petra-oid-1", "Petra Dvořáková"),
                Member("petra-oid-2", "Petra Dvořáková"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Total.Should().Be(5);
        response.AlreadyMigrated.Should().Be(2);
        response.Resolved.Should().Be(1);
        response.Ambiguous.Should().Be(1);
        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().HaveCount(2);
    }
}
```

- [ ] **Step 3: Run the handler test suite to confirm behavior is preserved (GREEN for behavioral tests)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BackfillArticleRequestedByHandlerTests"
```

Expected: all 8 tests in `BackfillArticleRequestedByHandlerTests` **PASS**.

- [ ] **Step 4: Re-run the architecture-boundary test (GREEN for boundary)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```

Expected: the `Article -> UserManagement` theory case now **PASSES**. All other rules (Leaflet → KB, Article → KB, Logistics → Manufacture, etc.) continue to pass with their existing allowlists.

---

## Task 6: Verify the new boundary rule is load-bearing

The architecture test is regression-prevention; we must prove it actually catches a UserManagement → Article violation by injecting one and confirming the test fails. This is a manual verification — do **not** commit the violation.

**Files:**
- Temporarily modify: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`

- [ ] **Step 1: Inject a sentinel violation**

At the top of `BackfillArticleRequestedByHandler.cs`, temporarily add the line below as the **first** using directive. This re-introduces the exact violation we just removed.

```csharp
using Anela.Heblo.Application.Features.UserManagement.Services;
```

Save the file. (The new `using` will be unused; that's fine — the architecture test inspects type references, not just whether the type is used in code. To be safe and avoid `CS8019 Unused using directive` from breaking the build under TreatWarningsAsErrors, also temporarily add a trivial reference inside `Handle`, e.g., declare `IGraphService? _unused = null;` as a local — but only if a sentinel using alone doesn't cause the rule to fire. Try the using-only approach first.)

- [ ] **Step 2: Run the architecture rule — confirm it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces"
```

Expected: the `Article -> UserManagement` theory case **FAILS** with a violation message naming the handler and an `IGraphService` (or similar UserManagement) type. This confirms the rule catches the violation.

If the test still passes (e.g., because the reflection-based scanner only inspects member signatures and not unused usings), instead inject a more reliable sentinel by adding a private field — e.g., `private readonly IGraphService? _sentinel = null;` — and re-run.

- [ ] **Step 3: Revert the sentinel**

Remove the temporary `using` line (and any temporary field/local you added) so the handler returns to its clean post-Task-5 state. Save the file.

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: all `ModuleBoundariesTests` theory cases and facts **PASS** again.

---

## Task 7: Final validation and commit

**Files:** none modified in this task; only verification commands.

- [ ] **Step 1: Confirm no Article file imports UserManagement**

Run the grep from FR-6 of the spec:

```bash
grep -rn "using Anela.Heblo.Application.Features.UserManagement" \
  backend/src/Anela.Heblo.Application/Features/Article/
```

Expected: **zero matches**. If matches appear, repeat Task 5 on the offending file (same pattern) or file a follow-up issue as noted in spec FR-6.

- [ ] **Step 2: Full backend build**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build **succeeds** with no warnings introduced by this change.

- [ ] **Step 3: Format check**

Run:
```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0 with no files reported as needing formatting. If it reports changes needed, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply formatting, then verify-no-changes again.

- [ ] **Step 4: Full test suite for affected projects**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: all tests **PASS**, including:
- All 8 `BackfillArticleRequestedByHandlerTests` cases.
- All `ModuleBoundariesTests` theory cases (now including `Article -> UserManagement`) and facts.

- [ ] **Step 5: Commit**

All six files modified together form a single atomic refactor. Stage and commit:

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs \
  backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs \
  backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs \
  backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs \
  backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs

git commit -m "$(cat <<'EOF'
refactor: decouple Article from UserManagement via IArticleUserResolver

BackfillArticleRequestedByHandler now consumes an Article-owned
IArticleUserResolver contract instead of UserManagement's IGraphService.
UserManagement provides an internal sealed GraphArticleUserResolver
adapter, registered once in AddUserManagement outside the mock/real
IGraphService branch so the binding works for both auth modes.

A new ModuleBoundariesTests rule (Article -> UserManagement) prevents
regressions.
EOF
)"
```

Expected: commit created successfully, repo working tree clean.

---

## Spec Coverage Check

- **FR-1** (define `IArticleUserResolver` contract) — Task 2.
- **FR-2** (refactor `BackfillArticleRequestedByHandler`) — Task 5 Step 1.
- **FR-3** (provide `GraphArticleUserResolver` adapter) — Task 3; arch-review Amendment 3 (`internal sealed`) applied.
- **FR-4** (register adapter in `UserManagementModule`) — Task 4; arch-review Amendment 1 (correct method name `AddUserManagement` + placement outside if/else) applied.
- **FR-5** (architecture test) — Task 1 (RED) + Task 6 (load-bearing verification); arch-review Amendment 4 (Domain + Persistence prefixes) applied.
- **FR-6** (verify nothing else in Article references UserManagement) — Task 7 Step 1.
- **FR-7 / NFR-5** (handler tests updated) — Task 5 Step 2; arch-review Amendment 2 (test file refactor enumerated explicitly) applied.
- **Arch-review Amendment 5** (XML `<summary>` on contract) — Task 2 Step 1.
- **NFR-1, NFR-2** — preserved by construction (no behavioral changes).
- **NFR-3** (compile-time graph excludes UserManagement) — enforced by Task 1 / Task 6.
- **NFR-4** (no public API or schema changes) — preserved by construction.
