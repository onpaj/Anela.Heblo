# Decouple UserDisplayNameResolver from Authorization Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Invert the `Shared/Users` ↔ Authorization dependency so `UserDisplayNameResolver` depends only on a consumer-owned `IUserDirectorySource` contract, implemented by an Authorization-owned adapter, closing the last unenforced cross-module boundary flagged by arch-review issue #4083.

**Architecture:** Add `IUserDirectorySource`/`UserDirectoryEntry` in a new `Application/Shared/Users/Contracts/` folder; implement it via `AuthorizationUserDirectorySourceAdapter` in `Application/Features/Authorization/Infrastructure/`; register the binding in `AuthorizationModule`; repoint `UserDisplayNameResolver` and its tests at the new contract; add a `ModuleBoundariesTests` rule so the boundary can never silently regress.

**Tech Stack:** .NET 8, MediatR/DI (`Microsoft.Extensions.DependencyInjection`), `Microsoft.Extensions.Caching.Memory`, xUnit + FluentAssertions + Moq.

---

### task: add-user-directory-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs`

- [ ] **Step 1: Write the contract file**

```csharp
namespace Anela.Heblo.Application.Shared.Users.Contracts;

/// <summary>
/// Shared/Users-owned abstraction over "give me every user in the directory". Implemented by
/// the Authorization module via an adapter (see development_guidelines.md, "Cross-Module
/// Communication Example"). No other module namespace may be referenced from this file.
/// </summary>
public interface IUserDirectorySource
{
    Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A minimal, provider-agnostic projection of a directory user.</summary>
public sealed class UserDirectoryEntry
{
    public string? EntraObjectId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Users/Contracts/IUserDirectorySource.cs
git commit -m "feat(users): add consumer-owned IUserDirectorySource contract"
```

---

### task: add-authorization-directory-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`

**Depends on:** add-user-directory-contract

- [ ] **Step 1: Write the adapter**

```csharp
using Anela.Heblo.Application.Shared.Users.Contracts;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.Authorization.Infrastructure;

internal sealed class AuthorizationUserDirectorySourceAdapter : IUserDirectorySource
{
    private readonly IAuthorizationRepository _repository;

    public AuthorizationUserDirectorySourceAdapter(IAuthorizationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _repository.GetAllUsersAsync(cancellationToken);

        return users
            .Select(u => new UserDirectoryEntry
            {
                EntraObjectId = u.EntraObjectId,
                Email = u.Email,
                DisplayName = u.DisplayName,
            })
            .ToList();
    }
}
```

- [ ] **Step 2: Register the DI binding in `AuthorizationModule.cs`**

Open `backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs`. Add the using and one registration line, keeping every existing line unchanged:

```csharp
using Anela.Heblo.Application.Features.Authorization.Infrastructure;
using Anela.Heblo.Application.Shared.Users.Contracts;
```

Inside `AddAuthorizationModule`, immediately after the existing `services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();` line, add:

```csharp
        services.AddScoped<IUserDirectorySource, AuthorizationUserDirectorySourceAdapter>();
```

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Authorization/Infrastructure/AuthorizationUserDirectorySourceAdapter.cs backend/src/Anela.Heblo.Application/Features/Authorization/AuthorizationModule.cs
git commit -m "feat(authorization): implement IUserDirectorySource via adapter"
```

---

### task: update-user-display-name-resolver

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs`

**Depends on:** add-user-directory-contract

- [ ] **Step 1: Update the failing tests first — repoint mocks/fixtures at the new contract**

Replace the full contents of `backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs`:

```csharp
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Application.Shared.Users.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Users;

public class UserDisplayNameResolverTests
{
    private readonly Mock<IUserDirectorySource> _directorySource = new();

    private UserDisplayNameResolver CreateResolver() =>
        new(_directorySource.Object, new MemoryCache(new MemoryCacheOptions()));

    private void SetupUsers(params UserDirectoryEntry[] users) =>
        _directorySource
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users.ToList());

    private static UserDirectoryEntry User(string? entraObjectId, string email, string displayName) =>
        new()
        {
            EntraObjectId = entraObjectId,
            Email = email,
            DisplayName = displayName,
        };

    [Fact]
    public async Task ResolveAsync_MapsEntraObjectIdToDisplayName()
    {
        SetupUsers(User("oid-1", "alice@anela.cz", "Alice Example"));

        var result = await CreateResolver().ResolveAsync(["oid-1"]);

        result["oid-1"].Should().Be("Alice Example");
    }

    [Fact]
    public async Task ResolveAsync_MapsEmailIdentifierToDisplayName()
    {
        // Article stores RequestedBy which may be an email rather than the Entra object id.
        SetupUsers(User("oid-1", "alice@anela.cz", "Alice Example"));

        var result = await CreateResolver().ResolveAsync(["alice@anela.cz"]);

        result["alice@anela.cz"].Should().Be("Alice Example");
    }

    [Fact]
    public async Task ResolveAsync_UnknownIdentifier_ResolvesToNull()
    {
        SetupUsers(User("oid-1", "alice@anela.cz", "Alice Example"));

        var result = await CreateResolver().ResolveAsync(["oid-unknown"]);

        result.Should().ContainKey("oid-unknown");
        result["oid-unknown"].Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToEmail_WhenDisplayNameMissing()
    {
        SetupUsers(User("oid-1", "alice@anela.cz", "   "));

        var result = await CreateResolver().ResolveAsync(["oid-1"]);

        result["oid-1"].Should().Be("alice@anela.cz");
    }

    [Fact]
    public async Task ResolveAsync_EmptyInput_DoesNotQueryRepository()
    {
        var result = await CreateResolver().ResolveAsync([]);

        result.Should().BeEmpty();
        _directorySource.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_CachesLookup_AcrossCalls()
    {
        SetupUsers(User("oid-1", "alice@anela.cz", "Alice Example"));
        var resolver = CreateResolver();

        await resolver.ResolveAsync(["oid-1"]);
        await resolver.ResolveAsync(["oid-1"]);

        _directorySource.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail to compile against the old resolver**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDisplayNameResolverTests"`
Expected: build FAILS — `UserDisplayNameResolver` constructor does not accept `IUserDirectorySource` yet (CS1503 or similar).

- [ ] **Step 3: Update `UserDisplayNameResolver.cs`**

Replace the full contents of `backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs`:

```csharp
using Anela.Heblo.Application.Shared.Users.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Shared.Users;

/// <summary>
/// Resolves user identifiers to display names by looking up the user directory.
/// The directory is small and changes rarely, so the whole identifier→name lookup is
/// cached briefly to avoid a full-table scan on every feedback page load.
/// </summary>
public sealed class UserDisplayNameResolver : IUserDisplayNameResolver
{
    private const string CacheKey = "UserDisplayNameResolver:Lookup";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IUserDirectorySource _directorySource;
    private readonly IMemoryCache _cache;

    public UserDisplayNameResolver(IUserDirectorySource directorySource, IMemoryCache cache)
    {
        _directorySource = directorySource;
        _cache = cache;
    }

    public async Task<IReadOnlyDictionary<string, string?>> ResolveAsync(
        IEnumerable<string> identifiers,
        CancellationToken cancellationToken = default)
    {
        var distinct = identifiers
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var lookup = await GetLookupAsync(cancellationToken);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in distinct)
        {
            result[id] = lookup.TryGetValue(id, out var name) ? name : null;
        }

        return result;
    }

    /// <summary>Cached identifier→display name map, keyed by both Entra object id and email.</summary>
    private async Task<IReadOnlyDictionary<string, string>> GetLookupAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        var users = await _directorySource.GetAllAsync(cancellationToken);

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            var displayName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Email;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(user.EntraObjectId))
            {
                lookup[user.EntraObjectId] = displayName;
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                lookup[user.Email] = displayName;
            }
        }

        _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)lookup, CacheTtl);
        return lookup;
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDisplayNameResolverTests"`
Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Users/UserDisplayNameResolver.cs backend/test/Anela.Heblo.Tests/Shared/Users/UserDisplayNameResolverTests.cs
git commit -m "refactor(users): repoint UserDisplayNameResolver at IUserDirectorySource"
```

---

### task: add-module-boundary-rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Depends on:** add-authorization-directory-adapter, update-user-display-name-resolver

- [ ] **Step 1: Add the empty allowlist field**

In `ModuleBoundariesTests.cs`, immediately after the existing
`private static readonly HashSet<string> AuthorizationUserManagementAllowlist = ...;` block (around
line 356), add:

```csharp
    // Allowlist for Shared.Users -> Authorization. Empty — AuthorizationUserDirectorySourceAdapter
    // (Authorization-owned, in Features.Authorization.Infrastructure) is the sole implementer of
    // Shared.Users.Contracts.IUserDirectorySource; it lives outside this rule's inspected namespace
    // prefix, so nothing under Shared.Users itself may ever reference Authorization directly.
    private static readonly HashSet<string> SharedUsersAuthorizationAllowlist = new(StringComparer.Ordinal);
```

- [ ] **Step 2: Add the rule to `Rules()`**

In the `Rules()` method, immediately after the existing `"Authorization -> UserManagement"` entry (the
first entry in the `TheoryData<ModuleBoundaryRule>`), add:

```csharp
        new ModuleBoundaryRule(
            Name: "Shared.Users -> Authorization",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Shared.Users",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Authorization",
                "Anela.Heblo.Application.Features.Authorization",
                "Anela.Heblo.Persistence.Features.Authorization",
            },
            Allowlist: SharedUsersAuthorizationAllowlist),
```

- [ ] **Step 3: Run the full boundary test suite**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: `Passed! - Failed: 0` across all theory cases, including the new `"Shared.Users -> Authorization"` case with zero violations. If it fails, the failure message lists the exact `Consumer -> Referenced` entries — resolve them by finishing `update-user-display-name-resolver` fully (do not add entries to `SharedUsersAuthorizationAllowlist` to make it pass; that defeats FR-6).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): enforce Shared.Users -> Authorization module boundary"
```

---

### task: verify-full-suite

**Files:** none (verification only)

**Depends on:** add-user-directory-contract, add-authorization-directory-adapter, update-user-display-name-resolver, add-module-boundary-rule

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: `Build succeeded.` with 0 errors, 0 new warnings attributable to this change.

- [ ] **Step 2: Full backend test run**

Run: `cd backend && dotnet test`
Expected: all tests pass, including `UserDisplayNameResolverTests` (6/6) and `ModuleBoundariesTests`
(all `[Theory]` cases including the new `"Shared.Users -> Authorization"` entry, plus the unrelated
`Application_types_should_not_reference_AspNetCore_namespaces` fact, all green).

- [ ] **Step 3: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports changes, run `dotnet format` (no `--verify-no-changes`) and re-stage/commit the formatting fix as its own commit.

- [ ] **Step 4: Confirm no consumer module needed a code change**

Run: `git diff --stat main...HEAD -- backend/src/Anela.Heblo.Application/Features/KnowledgeBase backend/src/Anela.Heblo.Application/Features/Article backend/src/Anela.Heblo.Application/Features/Leaflet backend/src/Anela.Heblo.Application/Features/Smartsupp`
Expected: empty output — none of the four consumer feature modules were touched, confirming
`IUserDisplayNameResolver`'s public contract stayed stable (NFR-3).

- [ ] **Step 5: Grep for any remaining direct reference (belt-and-suspenders manual check)**

Run: `grep -rn "IAuthorizationRepository\|Authorization.Entities" backend/src/Anela.Heblo.Application/Shared/Users/`
Expected: no output (empty match) — confirms FR-4's acceptance criterion by direct inspection, not just by the automated rule.

- [ ] **Step 6: Final commit (if any formatting fix was needed) and push**

```bash
git push
```
