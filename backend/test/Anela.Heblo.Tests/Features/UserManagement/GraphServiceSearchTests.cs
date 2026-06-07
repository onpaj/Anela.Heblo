using System.Net;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement;

public sealed class GraphServiceSearchTests
{
    private const string MicrosoftGraphClientName = "MicrosoftGraph";

    private const string SampleUsersResponse = """
{
  "value": [
    { "id": "11111111-1111-1111-1111-111111111111", "displayName": "Alice Example", "mail": "alice@example.com", "userPrincipalName": "alice@example.com" },
    { "id": "22222222-2222-2222-2222-222222222222", "displayName": "Bob Example", "mail": null, "userPrincipalName": "bob@example.com" }
  ]
}
""";

    private static GraphService BuildService(HttpMessageHandler handler, out Mock<IHttpClientFactory> factoryMock)
    {
        factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock.Setup(t => t.GetAccessTokenForAppAsync(
                "https://graph.microsoft.com/.default", It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("test-token");

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        return new GraphService(tokenMock.Object, cache, logger, factoryMock.Object);
    }

    [Fact]
    public async Task SearchUsersAsync_BuildsSearchRequest_AndParsesUsers()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleUsersResponse);
        var service = BuildService(handler, out var factoryMock);

        var result = await service.SearchUsersAsync("ali");

        factoryMock.Verify(f => f.CreateClient(MicrosoftGraphClientName), Times.Once);
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("11111111-1111-1111-1111-111111111111");
        result[0].Email.Should().Be("alice@example.com");
        result[1].Email.Should().Be("bob@example.com"); // falls back to UPN when mail is null

        handler.LastRequestUri!.ToString().Should().Contain("https://graph.microsoft.com/v1.0/users?");
        handler.LastRequestUri!.ToString().Should().Contain("$search=");
        handler.LastRequestHeaders!.Contains("ConsistencyLevel").Should().BeTrue();
    }

    [Fact]
    public async Task SearchUsersAsync_NonSuccess_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"Forbidden\"}}");
        var service = BuildService(handler, out _);

        var result = await service.SearchUsersAsync("ali");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyQuery_ReturnsEmpty_WithoutTouchingFactory()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleUsersResponse);
        var service = BuildService(handler, out var factoryMock);

        var result = await service.SearchUsersAsync("   ");

        result.Should().BeEmpty();
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SearchUsersAsync_TokenFailure_ReturnsEmpty_WithoutTouchingFactory()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleUsersResponse);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(MicrosoftGraphClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        var tokenMock = new Mock<ITokenAcquisition>();
        tokenMock.Setup(t => t.GetAccessTokenForAppAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()))
            .ThrowsAsync(new MsalUiRequiredException("err", "msg"));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new GraphService(tokenMock.Object, cache, Mock.Of<ILogger<GraphService>>(), factoryMock.Object);

        var result = await service.SearchUsersAsync("ali");

        result.Should().BeEmpty();
        factoryMock.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SearchUsersAsync_StripsDoubleQuotesFromQuery()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SampleUsersResponse);
        var service = BuildService(handler, out _);

        await service.SearchUsersAsync("ali\"ce");

        // The injected double-quote is removed before encoding, so the encoded term is "alice" not "ali"ce".
        handler.LastRequestUri!.ToString().Should().Contain("alice");
        handler.LastRequestUri!.ToString().Should().NotContain("ali%22ce");
    }
}
