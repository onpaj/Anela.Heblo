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
