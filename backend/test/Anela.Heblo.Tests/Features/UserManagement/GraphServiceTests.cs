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

    private static GraphService BuildServiceSequential(
        SequentialFakeHttpMessageHandler handler,
        out Mock<IHttpClientFactory> factoryMock)
    {
        factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object, configuration.Object);
    }

    private const string SpResponse = """
{
  "id": "sp-id-001",
  "appRoles": [
    {
      "id": "role-id-abc",
      "value": "Admin"
    }
  ]
}
""";

    private const string AssignmentsPageResponse = """
{
  "value": [
    {
      "appRoleId": "role-id-abc",
      "principalType": "User",
      "principalId": "aaaaaaaa-0001-0001-0001-000000000001"
    }
  ]
}
""";

    private static string AssignmentsPage21Response()
    {
        var assignments = Enumerable.Range(1, 21).Select(i =>
            $"{{\"appRoleId\":\"role-id-abc\",\"principalType\":\"User\",\"principalId\":\"aaaaaaaa-{i:D4}-{i:D4}-{i:D4}-{i:D12}\"}}"
        );
        return $"{{\"value\":[{string.Join(",", assignments)}]}}";
    }

    private const string BatchResponse1User = """
{
  "responses": [
    {
      "id": "0",
      "status": 200,
      "body": {
        "id": "aaaaaaaa-0001-0001-0001-000000000001",
        "displayName": "Alice Admin",
        "mail": "alice@example.com",
        "userPrincipalName": "alice@example.com"
      }
    }
  ]
}
""";

    private static string BatchResponseFor21Users(int startIndex, int count)
    {
        var resps = Enumerable.Range(0, count).Select(i =>
        {
            var n = startIndex + i;
            return $"{{\"id\":\"{i}\",\"status\":200,\"body\":{{\"id\":\"aaaaaaaa-{n:D4}-{n:D4}-{n:D4}-{n:D12}\",\"displayName\":\"User {n}\",\"mail\":\"user{n}@example.com\",\"userPrincipalName\":\"user{n}@example.com\"}}}}";
        });
        return $"{{\"responses\":[{string.Join(",", resps)}]}}";
    }

    private const string BatchResponseWithOneNon200 = """
{
  "responses": [
    {
      "id": "0",
      "status": 404,
      "body": { "error": { "code": "Request_ResourceNotFound" } }
    }
  ]
}
""";

    [Fact]
    public async Task GetAppRoleMembersAsync_SingleUser_IssuesOneBatchCall()
    {
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.OK, BatchResponse1User)
        );
        var service = BuildServiceSequential(handler, out _);

        var result = await service.GetAppRoleMembersAsync("Admin");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("aaaaaaaa-0001-0001-0001-000000000001");
        result[0].DisplayName.Should().Be("Alice Admin");
        result[0].Email.Should().Be("alice@example.com");

        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");
        handler.Requests[2].Method.Should().Be(HttpMethod.Post);
        handler.Requests[2].Body.Should().Contain("/users/aaaaaaaa-0001-0001-0001-000000000001");
        handler.Requests[2].Body.Should().NotContain("https://graph.microsoft.com");
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_TwentyOneUsers_IssuesTwoBatchCalls()
    {
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPage21Response()),
            (System.Net.HttpStatusCode.OK, BatchResponseFor21Users(1, 20)),
            (System.Net.HttpStatusCode.OK, BatchResponseFor21Users(21, 1))
        );
        var service = BuildServiceSequential(handler, out _);

        var result = await service.GetAppRoleMembersAsync("Admin");

        result.Should().HaveCount(21);
        handler.Requests.Should().HaveCount(4);
        handler.Requests[2].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");
        handler.Requests[3].Uri!.ToString().Should().Be("https://graph.microsoft.com/v1.0/$batch");

        var body1 = System.Text.Json.JsonDocument.Parse(handler.Requests[2].Body);
        body1.RootElement.GetProperty("requests").GetArrayLength().Should().Be(20);

        var body2 = System.Text.Json.JsonDocument.Parse(handler.Requests[3].Body);
        body2.RootElement.GetProperty("requests").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_NonTwoHundredSubResponse_SkipsUserAndLogsWarning()
    {
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.OK, BatchResponseWithOneNon200)
        );

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var loggerMock = new Mock<ILogger<GraphService>>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        var service = new GraphService(tokenMock.Object, cache, loggerMock.Object, factoryMock.Object, configuration.Object);

        var result = await service.GetAppRoleMembersAsync("Admin");

        result.Should().BeEmpty();

        loggerMock.Verify(
            l => l.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Could not resolve user")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_BatchLevelFailure_ReturnsEmptyListAndLogsError()
    {
        var handler = new SequentialFakeHttpMessageHandler(
            (System.Net.HttpStatusCode.OK, SpResponse),
            (System.Net.HttpStatusCode.OK, AssignmentsPageResponse),
            (System.Net.HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"ServiceNotAvailable\"}}")
        );

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default",
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var loggerMock = new Mock<ILogger<GraphService>>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["AzureAd:ClientId"]).Returns("test-client-id");

        var service = new GraphService(tokenMock.Object, cache, loggerMock.Object, factoryMock.Object, configuration.Object);

        var result = await service.GetAppRoleMembersAsync("Admin");

        result.Should().BeEmpty();

        loggerMock.Verify(
            l => l.Log(
                Microsoft.Extensions.Logging.LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Graph $batch request failed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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

    private sealed class SequentialFakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(System.Net.HttpStatusCode Status, string Body)> _responses;
        public List<(Uri? Uri, HttpMethod? Method, string Body)> Requests { get; } = new();

        public SequentialFakeHttpMessageHandler(params (System.Net.HttpStatusCode, string)[] responses)
        {
            _responses = new Queue<(System.Net.HttpStatusCode, string)>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string requestBody = string.Empty;
            if (request.Content is not null)
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add((request.RequestUri, request.Method, requestBody));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"No more queued responses. Unexpected call to {request.RequestUri}");

            var (status, body) = _responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
