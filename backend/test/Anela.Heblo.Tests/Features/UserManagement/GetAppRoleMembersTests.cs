using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Microsoft365.UserManagement;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public sealed class GetAppRoleMembersTests
{
    private const string MicrosoftGraphClientName = "MicrosoftGraph";
    private const string TestRoleValue = "Administration.Read";
    private const string TestClientId = "aaaabbbb-0000-1111-2222-ccccddddeeee";
    private const string TestSpId = "sp-id-0001-0002-0003";
    private const string TestAppRoleId = "role-id-1111-2222-3333";
    private const string TestUserId = "user-id-aaaa-bbbb-cccc";

    // SP response: contains the service principal id and one app role matching TestRoleValue
    private static readonly string SpResponse = $$"""
        {
          "id": "{{TestSpId}}",
          "appRoles": [
            {
              "id": "{{TestAppRoleId}}",
              "value": "{{TestRoleValue}}",
              "displayName": "Administration Read"
            }
          ]
        }
        """;

    // Assignment response: one User assignment matching the role
    private static readonly string AssignmentsResponse = $$"""
        {
          "value": [
            {
              "appRoleId": "{{TestAppRoleId}}",
              "principalType": "User",
              "principalId": "{{TestUserId}}"
            }
          ]
        }
        """;

    // Batch response for the single assigned user (Graph $batch format)
    private static readonly string BatchUserResponse = $$"""
        {
          "responses": [
            {
              "id": "0",
              "status": 200,
              "body": {
                "id": "{{TestUserId}}",
                "displayName": "Test User",
                "mail": "test.user@example.com",
                "userPrincipalName": "test.user@example.com"
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Builds a GraphService that drives HTTP calls through a queue handler,
    /// so each sequential Graph call gets its own pre-configured response.
    /// </summary>
    private static GraphService BuildService(
        QueuedHttpMessageHandler handler,
        string? clientId,
        out Mock<IHttpClientFactory> factoryMock,
        out IMemoryCache cache)
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
                It.IsAny<Microsoft.Identity.Web.TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        cache = new MemoryCache(new MemoryCacheOptions());

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["AzureAd:ClientId"]).Returns(clientId);

        return new GraphService(
            tokenMock.Object,
            cache,
            Mock.Of<ILogger<GraphService>>(),
            factoryMock.Object,
            configMock.Object);
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_CacheHit_ReturnsCachedValue_WithoutHttpCalls()
    {
        // Arrange
        var handler = new QueuedHttpMessageHandler();
        var service = BuildService(handler, TestClientId, out var factoryMock, out var cache);

        var cached = new List<UserDto>
        {
            new() { Id = "cached-user", DisplayName = "Cached", Email = "cached@example.com" }
        };
        cache.Set($"app_role_members_{TestRoleValue}", cached, TimeSpan.FromMinutes(20));

        // Act
        var result = await service.GetAppRoleMembersAsync(TestRoleValue);

        // Assert
        result.Should().BeEquivalentTo(cached);
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_MissingClientIdConfig_ReturnsEmptyList()
    {
        // Arrange — no responses queued because no HTTP calls should be made
        var handler = new QueuedHttpMessageHandler();
        var service = BuildService(handler, clientId: null, out var factoryMock, out _);

        // Act
        var result = await service.GetAppRoleMembersAsync(TestRoleValue);

        // Assert
        result.Should().BeEmpty();
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAppRoleMembersAsync_HappyPath_ReturnsResolvedUserDtos()
    {
        // Arrange: queue SP → assignments → user detail in the order GraphService calls them
        var handler = new QueuedHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SpResponse);
        handler.Enqueue(HttpStatusCode.OK, AssignmentsResponse);
        handler.Enqueue(HttpStatusCode.OK, BatchUserResponse);

        var service = BuildService(handler, TestClientId, out _, out _);

        // Act
        var result = await service.GetAppRoleMembersAsync(TestRoleValue);

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(TestUserId);
        result[0].DisplayName.Should().Be("Test User");
        result[0].Email.Should().Be("test.user@example.com");
    }

    /// <summary>
    /// Returns pre-configured responses in FIFO order, enabling tests for methods
    /// that make multiple sequential HTTP calls with different expected responses.
    /// </summary>
    private sealed class QueuedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public void Enqueue(HttpStatusCode status, string body) =>
            _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_responses.TryDequeue(out var entry))
                throw new InvalidOperationException(
                    $"No queued response for {request.RequestUri}. Add more Enqueue() calls.");

            return Task.FromResult(new HttpResponseMessage(entry.Status)
            {
                Content = new StringContent(entry.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
