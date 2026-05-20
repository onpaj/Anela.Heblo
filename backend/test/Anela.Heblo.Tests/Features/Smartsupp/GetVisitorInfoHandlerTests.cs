using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetVisitorInfoHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();

    private GetVisitorInfoHandler CreateHandler() =>
        new(_repo.Object, _apiClient.Object);

    private static SmartsuppConversation MakeConversation(
        string id = "c1",
        string? visitorId = "vis1",
        string? contactId = "ct1",
        DateTime? visitorInfoFetchedAt = null) =>
        new()
        {
            Id = id,
            VisitorId = visitorId,
            ContactId = contactId,
            Status = SmartsuppConversationStatus.Open,
            VisitorInfoFetchedAt = visitorInfoFetchedAt,
            Messages = [],
        };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation?)null);

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenNoVisitorId()
    {
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConversation(visitorId: null));

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppVisitorNotFound);
    }

    [Fact]
    public async Task Handle_CallsApiAndCaches_WhenCacheMiss()
    {
        var conv = MakeConversation(visitorInfoFetchedAt: null);
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation> { MakeConversation("c2"), MakeConversation("c3") });
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData
            {
                Id = "vis1",
                Os = "OS X",
                Browser = "Chrome",
                BrowserVersion = "148.0.0.0",
                UserAgent = "Mozilla...",
                VisitsCount = 321,
            });

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.VisitorInfo.Should().NotBeNull();
        result.VisitorInfo!.Os.Should().Be("OS X");
        result.VisitorInfo.Browser.Should().Be("Chrome");
        result.VisitorInfo.BrowserVersion.Should().Be("148.0.0.0");
        result.VisitorInfo.VisitsCount.Should().Be(321);
        result.VisitorInfo.ChatsCount.Should().Be(3); // c1 + c2 + c3

        _repo.Verify(r => r.UpdateVisitorCacheAsync(
            "c1", "Mozilla...", "OS X", "Chrome", "148.0.0.0", 321,
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UsesCachedData_WhenCacheFresh()
    {
        var conv = MakeConversation(visitorInfoFetchedAt: DateTime.UtcNow.AddHours(-1));
        conv.VisitorOs = "Windows 11";
        conv.VisitorBrowser = "Firefox";
        conv.VisitorBrowserVersion = "120.0";
        conv.VisitorVisitsCount = 50;
        conv.VisitorUserAgent = "Mozilla/5.0 Windows";

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation>());

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.VisitorInfo!.Os.Should().Be("Windows 11");
        result.VisitorInfo.VisitsCount.Should().Be(50);

        _apiClient.Verify(a => a.GetVisitorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpdateVisitorCacheAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RefreshesCache_WhenCacheStale()
    {
        var conv = MakeConversation(visitorInfoFetchedAt: DateTime.UtcNow.AddHours(-25));
        conv.VisitorOs = "Old OS";

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SmartsuppConversation>());
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData { Id = "vis1", Os = "New OS", Browser = "Chrome", BrowserVersion = "149.0" });

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.VisitorInfo!.Os.Should().Be("New OS");
        _apiClient.Verify(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsPageHistory_FromMessagePageUrls()
    {
        var conv = MakeConversation(visitorInfoFetchedAt: null);
        conv.Messages =
        [
            new() { Id = "m1", ConversationId = "c1", PageUrl = "https://www.anela.cz/shop", CreatedAt = new DateTime(2026, 5, 1, 10, 0, 0) },
            new() { Id = "m2", ConversationId = "c1", PageUrl = "https://www.anela.cz/checkout", CreatedAt = new DateTime(2026, 5, 1, 10, 5, 0) },
            new() { Id = "m3", ConversationId = "c1", PageUrl = "https://www.anela.cz/shop", CreatedAt = new DateTime(2026, 5, 1, 10, 2, 0) }, // duplicate
        ];

        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(conv);
        _repo.Setup(r => r.ListConversationsForContactAsync("ct1", "c1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _apiClient.Setup(a => a.GetVisitorAsync("vis1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppVisitorData { Id = "vis1" });

        var result = await CreateHandler().Handle(
            new GetVisitorInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.VisitorInfo!.Pages.Should().HaveCount(2); // deduplicated
        result.VisitorInfo.Pages.Select(p => p.Url).Should().Contain("https://www.anela.cz/shop");
        result.VisitorInfo.Pages.Select(p => p.Url).Should().Contain("https://www.anela.cz/checkout");
    }
}
