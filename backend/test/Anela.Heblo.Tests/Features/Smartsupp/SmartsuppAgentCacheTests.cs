using System.Reflection;
using Anela.Heblo.Application.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppAgentCacheTests
{
    /// <summary>
    /// Builds a mocked IServiceScopeFactory -> IServiceScope -> IServiceProvider chain that
    /// resolves ISmartsuppApiClient to a controllable mock, mirroring the scope-per-refresh
    /// pattern SmartsuppAgentCache uses in production (see RunDqtHandlerTests.cs for the same
    /// technique against a different class).
    /// </summary>
    private static (Mock<IServiceScopeFactory> Factory, Mock<ISmartsuppApiClient> ApiClient) BuildScopeFactory()
    {
        var apiClientMock = new Mock<ISmartsuppApiClient>();

        var providerMock = new Mock<IServiceProvider>();
        providerMock.Setup(p => p.GetService(typeof(ISmartsuppApiClient))).Returns(apiClientMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);

        var factoryMock = new Mock<IServiceScopeFactory>();
        factoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return (factoryMock, apiClientMock);
    }

    private static SmartsuppAgentCache CreateSut(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, NullLogger<SmartsuppAgentCache>.Instance);

    /// <summary>
    /// SmartsuppAgentCache's 1-hour TTL fast-path (line 38: "_cache is not null &&
    /// DateTime.UtcNow - _cachedAt &lt; CacheTtl") means a successful call is never re-fetched
    /// by simply calling GetAgentNamesAsync() again. To deterministically force the code back
    /// into the refresh/catch path for the warm-cache-failure tests, this reflection helper
    /// resets the private _cachedAt field to a value older than the TTL. This is a test-only
    /// technique (no production code change) with existing precedent in this test project, e.g.
    /// CatalogRepositoryTests.cs's use of BindingFlags.NonPublic | BindingFlags.Instance.
    /// </summary>
    private static void ExpireCache(SmartsuppAgentCache instance)
    {
        var field = typeof(SmartsuppAgentCache).GetField("_cachedAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(instance, DateTime.MinValue);
    }

    [Fact]
    public async Task GetAgentNamesAsync_SuccessfulFetch_ExcludesAgentsWithNullName()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
            new() { Id = "agent-2", Name = null },
            new() { Id = "agent-3", Name = "Petr Svoboda" },
        };
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(agents);
        var sut = CreateSut(factory.Object);

        // Act
        var result = await sut.GetAgentNamesAsync();

        // Assert
        result.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["agent-1"] = "Jana Novakova",
            ["agent-3"] = "Petr Svoboda",
        });
    }

    [Fact]
    public async Task GetAgentNamesAsync_CalledTwiceWithinTtl_OnlyFetchesFromApiOnce()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        var agents = new List<SmartsuppAgentData>
        {
            new() { Id = "agent-1", Name = "Jana Novakova" },
        };
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(agents);
        var sut = CreateSut(factory.Object);

        // Act
        var first = await sut.GetAgentNamesAsync();
        var second = await sut.GetAgentNamesAsync();

        // Assert
        first.Should().BeEquivalentTo(second);
        apiClient.Verify(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAgentNamesAsync_ColdCacheApiThrows_ReturnsEmptyDictionaryWithoutThrowing()
    {
        // Arrange
        var (factory, apiClient) = BuildScopeFactory();
        apiClient.Setup(c => c.GetAgentsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Smartsupp API unavailable"));
        var sut = CreateSut(factory.Object);

        // Act
        // If GetAgentNamesAsync rethrew instead of falling back, this await would throw and
        // fail the test — so a passing test also proves FR-1's "must not throw" requirement.
        var result = await sut.GetAgentNamesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
