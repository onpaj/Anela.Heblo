using System.Net;
using Anela.Heblo.Application.Features.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GraphServiceTests
{
    private const string MicrosoftGraphClientName = "MicrosoftGraph";

    private const string SampleGraphResponse = """
{
  "value": [
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "11111111-1111-1111-1111-111111111111",
      "displayName": "Alice Example",
      "mail": "alice@example.com",
      "userPrincipalName": "alice@example.com"
    },
    {
      "@odata.type": "#microsoft.graph.user",
      "id": "22222222-2222-2222-2222-222222222222",
      "displayName": "Bob Example",
      "mail": null,
      "userPrincipalName": "bob@example.com"
    },
    {
      "@odata.type": "#microsoft.graph.group",
      "id": "33333333-3333-3333-3333-333333333333",
      "displayName": "Nested Group"
    }
  ]
}
""";

    [Fact]
    public void Constructor_AcceptsIHttpClientFactory_AsFourthParameter()
    {
        // Arrange
        var tokenAcquisition = Mock.Of<ITokenAcquisition>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        var httpClientFactory = Mock.Of<IHttpClientFactory>();

        // Act
        var service = new GraphService(tokenAcquisition, cache, logger, httpClientFactory);

        // Assert
        service.Should().NotBeNull();
    }

    private static GraphService BuildService(
        HttpMessageHandler handler,
        out Mock<IHttpClientFactory> factoryMock,
        out Mock<ITokenAcquisition> tokenMock,
        out IMemoryCache cache)
    {
        factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();

        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object);
    }

    [Fact]
    public async Task GetGroupMembersAsync_CacheMiss_InvokesFactory_AndReturnsParsedUsers()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleGraphResponse);
        var service = BuildService(handler, out var factoryMock, out _, out _);

        // Act
        var result = await service.GetGroupMembersAsync("group-1");

        // Assert
        factoryMock.Verify(f => f.CreateClient(MicrosoftGraphClientName), Times.Once);
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("11111111-1111-1111-1111-111111111111");
        result[0].DisplayName.Should().Be("Alice Example");
        result[0].Email.Should().Be("alice@example.com");
        result[1].Id.Should().Be("22222222-2222-2222-2222-222222222222");
        result[1].Email.Should().Be("bob@example.com");

        handler.LastRequestUri!.ToString()
            .Should().Be("https://graph.microsoft.com/v1.0/groups/group-1/members?$select=id,displayName,mail,userPrincipalName");
        handler.LastMethod.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public void AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(Mock.Of<ITokenAcquisition>());
        var configuration = new ConfigurationBuilder().Build(); // no mock-auth keys => production branch

        // Act
        services.AddUserManagement(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
        resolved.Should().BeOfType<GraphService>();

        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("MicrosoftGraph");
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddUserManagement_MockBranch_RegistersMockGraphService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseMockAuth"] = "true"
            })
            .Build();

        // Act
        services.AddUserManagement(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
        resolved.Should().BeOfType<MockGraphService>();
    }
}
