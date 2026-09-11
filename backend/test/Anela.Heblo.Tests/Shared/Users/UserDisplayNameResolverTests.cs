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
