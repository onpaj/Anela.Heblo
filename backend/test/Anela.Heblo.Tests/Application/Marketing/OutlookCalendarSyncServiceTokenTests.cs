using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Microsoft365;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Domain.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Moq.Protected;
using System.Security.Claims;

namespace Anela.Heblo.Tests.Application.Marketing;

public class OutlookCalendarSyncServiceTokenTests
{
    private readonly Mock<ITokenAcquisition> _tokenAcquisition = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<IMarketingCategoryMapper> _mapper = new();
    private readonly Mock<ILogger<OutlookCalendarSyncService>> _logger = new();

    private const string DelegatedScope = "https://graph.microsoft.com/Group.ReadWrite.All";
    private const string AppScope = "https://graph.microsoft.com/.default";

    private OutlookCalendarSyncService BuildService()
    {
        var options = Options.Create(new MarketingCalendarOptions
        {
            GroupId = "test-group-id",
            PushEnabled = true,
        });
        _mapper.Setup(x => x.MapToOutlookCategory(It.IsAny<MarketingActionType>())).Returns("Blog");
        return new OutlookCalendarSyncService(
            _tokenAcquisition.Object,
            _httpClientFactory.Object,
            options,
            _mapper.Object,
            _logger.Object);
    }

    private HttpClient BuildHttpClient(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        return new HttpClient(handler.Object);
    }

    private static MarketingAction BuildAction()
    {
        var action = new MarketingActionTestBuilder()
            .WithId(1)
            .WithTitle("Test")
            .WithActionType(MarketingActionType.Blog)
            .WithStartDate(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithCreatedAt(DateTime.UtcNow)
            .WithModifiedAt(DateTime.UtcNow)
            .WithCreatedBy("user-1")
            .WithOutlookEventId("existing-event-id")
            .Build();
        return action;
    }

    [Fact]
    public async Task CreateEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.Created, @"{""id"":""new-event-id""}"));

        var service = BuildService();
        await service.CreateEventAsync(BuildAction(), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.OK, "{}"));

        var service = BuildService();
        await service.UpdateEventAsync(BuildAction(), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteEventAsync_UsesDelegatedToken_NotAppToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("delegated-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.NoContent, ""));

        var service = BuildService();
        await service.DeleteEventAsync("event-id", CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.Is<IEnumerable<string>>(s => s.Contains(DelegatedScope)),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task ListEventsAsync_UsesAppToken_NotDelegatedToken()
    {
        _tokenAcquisition
            .Setup(x => x.GetAccessTokenForAppAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("app-token");
        _httpClientFactory.Setup(x => x.CreateClient("MicrosoftGraph"))
            .Returns(BuildHttpClient(HttpStatusCode.OK, @"{""value"":[]}"));

        var service = BuildService();
        await service.ListEventsAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(7), CancellationToken.None);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForAppAsync(
            It.Is<string>(s => s == AppScope),
            It.IsAny<string?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Once);

        _tokenAcquisition.Verify(x => x.GetAccessTokenForUserAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<TokenAcquisitionOptions?>()),
            Times.Never);
    }
}
