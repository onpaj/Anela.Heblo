using System.Net;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
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

        var configuration = Mock.Of<IConfiguration>();

        // Act
        var service = new GraphService(tokenAcquisition, cache, logger, httpClientFactory, configuration);

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
        var configuration = Mock.Of<IConfiguration>();

        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object, configuration);
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
        services.AddSingleton<IConfiguration>(configuration);

        // Act — IGraphService is registered by the adapter layer, not UserManagement
        services.AddMicrosoft365Adapter(configuration);

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

        // Act — IGraphService is registered by the adapter layer, not UserManagement
        services.AddMicrosoft365Adapter(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<IGraphService>();
        resolved.Should().BeOfType<MockGraphService>();
    }

    [Fact]
    public async Task GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "should not be called");
        var service = BuildService(handler, out var factoryMock, out _, out var cache);

        var cached = new List<UserDto>
        {
            new() { Id = "cached-1", DisplayName = "Cached User", Email = "cached@example.com" }
        };
        cache.Set("group_members_group-1", cached, TimeSpan.FromMinutes(20));

        // Act
        var result = await service.GetGroupMembersAsync("group-1");

        // Assert
        result.Should().BeEquivalentTo(cached);
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
        handler.LastRequestUri.Should().BeNull();
    }

    [Fact]
    public async Task GetGroupMembersAsync_TokenAcquisitionMsalException_Throws()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleGraphResponse);
        var service = BuildService(handler, out var factoryMock, out var tokenMock, out _);

        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ThrowsAsync(new MsalUiRequiredException("err", "msg"));

        // Act & Assert
        await Assert.ThrowsAsync<MsalUiRequiredException>(() => service.GetGroupMembersAsync("group-1"));
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
        var service = BuildService(handler, out _, out _, out _);

        // Act
        var result = await service.GetGroupMembersAsync("group-1");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupMembersAsync_TransportThrows_Throws()
    {
        // Arrange
        var throwingHandler = new ThrowingHttpMessageHandler(new HttpRequestException("boom"));
        var service = BuildService(throwingHandler, out _, out _, out _);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetGroupMembersAsync("group-1"));
    }

    [Fact]
    public async Task GetGroupMembersAsync_DoesNotDispose_FactoryProvidedClient()
    {
        // Arrange
        var tracker = new DisposalTrackingHandler(HttpStatusCode.OK, SampleGraphResponse);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient("MicrosoftGraph"))
            .Returns(() => new HttpClient(tracker, disposeHandler: true));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        var configuration = Mock.Of<IConfiguration>();
        var service = new GraphService(tokenMock.Object, cache, logger, factoryMock.Object, configuration);

        // Act
        await service.GetGroupMembersAsync("group-1");

        // Assert
        tracker.DisposeCount.Should().Be(0,
            "GraphService must not dispose the HttpClient returned by IHttpClientFactory — disposal is the factory's responsibility.");
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHttpMessageHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }

    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public int DisposeCount { get; private set; }

        public DisposalTrackingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
