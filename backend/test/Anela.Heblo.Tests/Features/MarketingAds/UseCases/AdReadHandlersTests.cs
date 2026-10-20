using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdAccounts;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdChangeHistory;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;
using Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.UseCases;

public class AdReadHandlersTests
{
    private static readonly DateOnly To = new(2026, 10, 6);
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private readonly Mock<IAdsReadRepository> _repo = new();

    public AdReadHandlersTests() => _repo.SetupGet(r => r.IsAvailable).Returns(true);

    private GetAdPerformanceRequest Perf(Action<GetAdPerformanceRequest>? change = null)
    {
        var request = new GetAdPerformanceRequest
        {
            Platform = AdPlatform.GoogleAds,
            AccountExternalId = "acc",
            Level = AdEntityLevel.Campaign,
            From = To.AddDays(-6),
            To = To,
        };
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task Unavailable_database_is_a_configuration_error_for_every_handler()
    {
        _repo.SetupGet(r => r.IsAvailable).Returns(false);

        var responses = new BaseResponse[]
        {
            await new GetAdAccountsHandler(_repo.Object).Handle(new GetAdAccountsRequest(), default),
            await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(), default),
            await new GetAdSearchTermsHandler(_repo.Object).Handle(new GetAdSearchTermsRequest { AccountExternalId = "acc", From = To, To = To }, default),
            await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest { From = To, To = To }, default),
            await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now)).Handle(new GetAdChangeHistoryRequest(), default),
        };

        responses.Should().OnlyContain(r => !r.Success && r.ErrorCode == ErrorCodes.ConfigurationError && r.Params!["Reason"] == "AdsDatabaseNotConfigured");
    }

    [Theory]
    [InlineData(1, 0)]   // From after To
    [InlineData(-93, 0)] // 94 days
    public async Task Performance_rejects_bad_ranges(int fromOffset, int toOffset)
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.From = To.AddDays(fromOffset); r.To = To.AddDays(toOffset); }), default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        _repo.Verify(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 100, "PageNumber")]
    [InlineData(-1, 100, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 501, "PageSize")]
    public async Task Performance_rejects_bad_paging(int page, int size, string field)
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.PageNumber = page; r.PageSize = size; }), default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be(field);
    }

    [Fact]
    public async Task Performance_requires_an_account()
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => r.AccountExternalId = " "), default);

        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Performance_unknown_account_is_not_found()
    {
        _repo.Setup(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdPerformancePage?)null);

        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(), default);

        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        response.Params!["Account"].Should().Be("acc");
    }

    [Fact]
    public async Task Performance_passes_the_query_and_maps_the_page()
    {
        var page = new AdPerformancePage("CZK", 7, new[] { new AdPerformanceRowDto { EntityExternalId = "c-1", Cost = 10m } });
        _repo.Setup(r => r.GetPerformanceAsync(
                new AdPerformanceQuery(AdPlatform.GoogleAds, "acc", AdEntityLevel.Campaign, To.AddDays(-6), To, "p", 2, 50),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.ParentExternalId = "p"; r.PageNumber = 2; r.PageSize = 50; }), default);

        response.Success.Should().BeTrue();
        response.Currency.Should().Be("CZK");
        response.TotalCount.Should().Be(7);
        response.Rows.Should().ContainSingle().Which.EntityExternalId.Should().Be("c-1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public async Task Search_terms_reject_bad_top(int top)
    {
        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.Sklik, AccountExternalId = "acc", From = To, To = To, Top = top }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be("Top");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-93, 0)]
    public async Task Search_terms_reject_bad_ranges(int fromOffset, int toOffset)
    {
        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.GoogleAds, AccountExternalId = "acc", From = To.AddDays(fromOffset), To = To.AddDays(toOffset) }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task Search_terms_require_an_account()
    {
        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { AccountExternalId = "", From = To, To = To }, default);

        response.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
    }

    [Fact]
    public async Task Search_terms_unknown_account_is_not_found()
    {
        _repo.Setup(r => r.GetSearchTermsAsync(It.IsAny<AdSearchTermQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AdSearchTermRowDto>?)null);

        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.GoogleAds, AccountExternalId = "acc", From = To, To = To }, default);

        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        response.Params!["Account"].Should().Be("acc");
    }

    [Fact]
    public async Task Search_terms_return_the_repository_rows()
    {
        _repo.Setup(r => r.GetSearchTermsAsync(It.IsAny<AdSearchTermQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdSearchTermRowDto> { new() { SearchTerm = "zdarma", Cost = 3m } });

        var response = await new GetAdSearchTermsHandler(_repo.Object).Handle(
            new GetAdSearchTermsRequest { Platform = AdPlatform.Sklik, AccountExternalId = "acc", From = To.AddDays(-30), To = To }, default);

        response.Terms.Should().ContainSingle().Which.SearchTerm.Should().Be("zdarma");
    }

    [Fact]
    public async Task Blended_rejects_inverted_range()
    {
        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(
            new GetBlendedPerformanceRequest { From = To, To = To.AddDays(-1) }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task Blended_rejects_more_than_731_days()
    {
        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(
            new GetBlendedPerformanceRequest { From = To.AddDays(-731), To = To }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task Blended_missing_view_is_a_configuration_error()
    {
        _repo.Setup(r => r.GetBlendedDailyAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AdBlendedDayRow>?)null);

        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest { From = To.AddDays(-30), To = To }, default);

        response.ErrorCode.Should().Be(ErrorCodes.ConfigurationError);
        response.Params!["Reason"].Should().Be("BlendedViewMissing");
    }

    [Fact]
    public async Task Blended_aggregates_by_requested_grain_with_total()
    {
        _repo.Setup(r => r.GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AdBlendedDayRow { Date = new DateOnly(2026, 9, 1), AdCostCzk = 100m, RevenueWithoutVatCzk = 1000m, Orders = 2 },
                new AdBlendedDayRow { Date = new DateOnly(2026, 9, 2), AdCostCzk = 100m, RevenueWithoutVatCzk = 1000m, Orders = 3 },
            });

        var response = await new GetBlendedPerformanceHandler(_repo.Object).Handle(new GetBlendedPerformanceRequest
        {
            From = new DateOnly(2026, 9, 1),
            To = new DateOnly(2026, 9, 30),
            Granularity = BlendedGranularity.Monthly,
        }, default);

        response.Periods.Should().ContainSingle().Which.Orders.Should().Be(5);
        response.Total.BlendedRoas.Should().Be(10.0000m);
    }

    [Fact]
    public async Task Change_history_defaults_to_the_last_30_days()
    {
        _repo.Setup(r => r.GetChangeEventsAsync(It.IsAny<AdChangeEventQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdChangeEventPage(0, Array.Empty<AdChangeEventDto>()));

        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { Origin = AdChangeOrigin.OutOfBand }, default);

        response.From.Should().Be(Now.AddDays(-30));
        response.To.Should().Be(Now);
        _repo.Verify(r => r.GetChangeEventsAsync(
            It.Is<AdChangeEventQuery>(q => q.Origin == AdChangeOrigin.OutOfBand && q.From == Now.AddDays(-30) && q.PageSize == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Change_history_rejects_more_than_92_days()
    {
        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { From = Now.AddDays(-100), To = Now }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task Change_history_rejects_inverted_range()
    {
        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { From = Now, To = Now.AddDays(-1) }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
    }

    [Theory]
    [InlineData(0, 100, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 501, "PageSize")]
    public async Task Change_history_rejects_bad_paging(int page, int size, string field)
    {
        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { PageNumber = page, PageSize = size }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be(field);
        _repo.Verify(r => r.GetChangeEventsAsync(It.IsAny<AdChangeEventQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Performance_rejects_page_number_that_would_overflow_the_offset()
    {
        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => { r.PageNumber = 2_000_000_000; r.PageSize = 500; }), default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be("PageNumber");
        _repo.Verify(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Change_history_rejects_page_number_that_would_overflow_the_offset()
    {
        var response = await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { PageNumber = 2_000_000_000, PageSize = 500 }, default);

        response.ErrorCode.Should().Be(ErrorCodes.InvalidValue);
        response.Params!["Field"].Should().Be("PageNumber");
        _repo.Verify(r => r.GetChangeEventsAsync(It.IsAny<AdChangeEventQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Change_history_trims_the_account_id()
    {
        _repo.Setup(r => r.GetChangeEventsAsync(It.IsAny<AdChangeEventQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdChangeEventPage(0, Array.Empty<AdChangeEventDto>()));

        await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now))
            .Handle(new GetAdChangeHistoryRequest { AccountExternalId = "  acc " }, default);

        _repo.Verify(r => r.GetChangeEventsAsync(It.Is<AdChangeEventQuery>(q => q.AccountExternalId == "acc"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Performance_unknown_account_reports_the_trimmed_id()
    {
        _repo.Setup(r => r.GetPerformanceAsync(It.IsAny<AdPerformanceQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync((AdPerformancePage?)null);

        var response = await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => r.AccountExternalId = " acc "), default);

        response.Params!["Account"].Should().Be("acc");
    }

    [Fact]
    public async Task Undefined_enum_values_are_rejected_by_every_handler()
    {
        var responses = new BaseResponse[]
        {
            await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => r.Platform = (AdPlatform)99), default),
            await new GetAdPerformanceHandler(_repo.Object).Handle(Perf(r => r.Level = (AdEntityLevel)99), default),
            await new GetAdSearchTermsHandler(_repo.Object).Handle(
                new GetAdSearchTermsRequest { Platform = (AdPlatform)99, AccountExternalId = "acc", From = To, To = To }, default),
            await new GetBlendedPerformanceHandler(_repo.Object).Handle(
                new GetBlendedPerformanceRequest { From = To, To = To, Granularity = (BlendedGranularity)99 }, default),
            await new GetAdChangeHistoryHandler(_repo.Object, new FakeTimeProvider(Now)).Handle(
                new GetAdChangeHistoryRequest { Origin = (AdChangeOrigin)99 }, default),
        };

        responses.Should().OnlyContain(r => !r.Success && r.ErrorCode == ErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task List_accounts_passes_filters()
    {
        _repo.Setup(r => r.ListAccountsAsync(AdPlatform.MetaAds, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AdAccountDto> { new() { ExternalId = "act_1" } });

        var response = await new GetAdAccountsHandler(_repo.Object).Handle(new GetAdAccountsRequest { Platform = AdPlatform.MetaAds, IncludeUnmanaged = true }, default);

        response.Accounts.Should().ContainSingle().Which.ExternalId.Should().Be("act_1");
    }
}
