using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Users;

public class UserDisplayNameResolverTests
{
    private readonly Mock<IAuthorizationRepository> _repository = new();

    private UserDisplayNameResolver CreateResolver() =>
        new(_repository.Object, new MemoryCache(new MemoryCacheOptions()));

    private void SetupUsers(params AppUser[] users) =>
        _repository
            .Setup(r => r.GetAllUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users.ToList());

    private static AppUser User(string? entraObjectId, string email, string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
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
        _repository.Verify(r => r.GetAllUsersAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_CachesLookup_AcrossCalls()
    {
        SetupUsers(User("oid-1", "alice@anela.cz", "Alice Example"));
        var resolver = CreateResolver();

        await resolver.ResolveAsync(["oid-1"]);
        await resolver.ResolveAsync(["oid-1"]);

        _repository.Verify(r => r.GetAllUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
