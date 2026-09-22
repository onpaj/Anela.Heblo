using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SyncProductPricesHandlerTests
{
    private readonly Mock<IPriceComparisonService> _comparisonService = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceChangeLogRepository> _changeLog = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly FakeTimeProvider _timeProvider = new();

    public SyncProductPricesHandlerTests()
    {
        _currentUser
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("id", "Operator", "operator@anela.cz", true));

        _erpReader
            .Setup(c => c.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProductPriceErp>());
    }

    private SyncProductPricesHandler CreateHandler() => new(
        _comparisonService.Object,
        _erpReader.Object,
        _erpWriter.Object,
        _changeLog.Object,
        _currentUser.Object,
        _timeProvider,
        NullLogger<SyncProductPricesHandler>.Instance);

    private static PriceDivergenceRowDto Row(
        string code, PriceDivergenceKind kind, decimal? shoptet = 390m, decimal? flexi = 350m) =>
        new()
        {
            ProductCode = code,
            ProductName = code,
            ShoptetPriceWithVat = shoptet,
            FlexiPriceWithVat = flexi,
            Kind = kind,
        };

    /// <summary>Report reads return <paramref name="reports"/> in order, the last one repeating.</summary>
    private void SetUpReports(params PriceComparisonResult[] reports)
    {
        var index = 0;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => reports[Math.Min(index++, reports.Length - 1)]);
    }

    private static PriceComparisonResult ReportOf(params PriceDivergenceRowDto[] rows) =>
        new() { Rows = rows.ToList() };

    /// <summary>
    /// Stubbed on <c>forceReload: false</c> specifically. The handler leans on
    /// <c>BuildScopedReportAsync</c> having just reloaded the ceník cache, so reloading again
    /// would mean a second live ERP round-trip on every sync — a stub on <c>It.IsAny&lt;bool&gt;()</c>
    /// would let that change land green.
    /// </summary>
    private void SetUpErpItems(params (string Code, int ErpItemId)[] items) =>
        _erpReader
            .Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.Select(i => new ProductPriceErp { ProductCode = i.Code, ErpItemId = i.ErpItemId }));

    private Task<SyncProductPricesResponse> HandleAsync(params string[] codes) =>
        CreateHandler().Handle(
            new SyncProductPricesRequest { ProductCodes = codes.ToList() }, CancellationToken.None);

    [Fact]
    public async Task writes_the_shoptet_price_into_flexi_for_a_divergent_row()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers, shoptet: 390m, flexi: 350m)));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(42, 390m, It.IsAny<CancellationToken>()), Times.Once);
        response.WrittenCount.Should().Be(1);
        response.FailedCount.Should().Be(0);
    }

    // Writing these would not settle them: the unknown-ness is in what the ERP read exposes,
    // not in what the ceník holds, so the row would come back unknown and be rewritten on
    // every sync for ever.
    [Theory]
    [InlineData(PriceDivergenceKind.FlexiPriceTypeUnknown)]
    [InlineData(PriceDivergenceKind.FlexiVatRateUnknown)]
    [InlineData(PriceDivergenceKind.InAgreement)]
    [InlineData(PriceDivergenceKind.MissingInShoptet)]
    [InlineData(PriceDivergenceKind.MissingInFlexi)]
    public async Task leaves_rows_that_need_no_write_alone(PriceDivergenceKind kind)
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", kind)));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        _erpWriter.Verify(
            w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        response.WrittenCount.Should().Be(0);
        response.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task returns_the_report_re_read_after_the_writes_landed()
    {
        // Arrange — the second read sees what the write put into Flexi
        SetUpReports(
            ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers, shoptet: 390m, flexi: 350m)),
            ReportOf(Row("A", PriceDivergenceKind.InAgreement, shoptet: 390m, flexi: 390m)));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.Rows.Should().ContainSingle()
            .Which.Kind.Should().Be(PriceDivergenceKind.InAgreement);
    }

    [Fact]
    public async Task does_not_re_read_both_live_systems_when_nothing_was_written()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.InAgreement)));

        // Act
        var response = await HandleAsync("A");

        // Assert
        _comparisonService.Verify(
            s => s.BuildScopedReportAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        response.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task keeps_writing_the_rest_of_the_selection_when_one_write_fails()
    {
        // Arrange
        SetUpReports(ReportOf(
            Row("A", PriceDivergenceKind.FlexiDiffers),
            Row("B", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42), ("B", 43));
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi said no"));

        // Act
        var response = await HandleAsync("A", "B");

        // Assert
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(43, 390m, It.IsAny<CancellationToken>()), Times.Once);
        response.WrittenCount.Should().Be(1);
        response.FailedCount.Should().Be(1);
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task reports_a_row_with_no_flexi_cenik_id_as_failed_without_writing()
    {
        // Arrange — writing by code would create a new Flexi ceník item
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 0));

        // Act
        var response = await HandleAsync("A");

        // Assert
        _erpWriter.Verify(
            w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        response.WrittenCount.Should().Be(0);
        response.FailedCount.Should().Be(1);

        // Nothing was attempted, and a missing id is a standing fact about the product rather
        // than an event — logging it per click would fill the history with the same row.
        _changeLog.Verify(
            l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task refuses_to_push_a_non_positive_shoptet_price_into_the_live_erp()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers, shoptet: 0m, flexi: 350m)));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert — not even attempted, so it is not a failure either; the row stays divergent
        _erpWriter.Verify(
            w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        response.WrittenCount.Should().Be(0);
        response.FailedCount.Should().Be(0);
    }

    // A cancelled request is the operator or the gateway walking away. The write may well have
    // landed, so recording it as failed would put a lie in the one place that is supposed to be
    // the record of what happened.
    [Fact]
    public async Task aborts_without_logging_false_failures_when_the_request_is_cancelled()
    {
        // Arrange
        SetUpReports(ReportOf(
            Row("A", PriceDivergenceKind.FlexiDiffers),
            Row("B", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42), ("B", 43));
        using var cancellation = new CancellationTokenSource();
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var sync = () => CreateHandler().Handle(
            new SyncProductPricesRequest { ProductCodes = new List<string> { "A", "B" } },
            cancellation.Token);

        // Assert
        await sync.Should().ThrowAsync<OperationCanceledException>();
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(43, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        _changeLog.Verify(
            l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Flexi's own 5-minute timeout cancels the HTTP call without cancelling the request, and
    // that IS this row's write failing.
    [Fact]
    public async Task counts_a_flexi_side_timeout_as_a_failed_write()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Flexi timed out", new TimeoutException()));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.FailedCount.Should().Be(1);
    }

    // The gateway drops a request at 230s. A run that outlives it reports a failure to an
    // operator whose prices did reach the ERP, so the loop stops itself first.
    [Fact]
    public async Task stops_writing_once_the_write_budget_is_spent_and_says_how_many_are_left()
    {
        // Arrange
        SetUpReports(ReportOf(
            Row("A", PriceDivergenceKind.FlexiDiffers),
            Row("B", PriceDivergenceKind.FlexiDiffers),
            Row("C", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42), ("B", 43), ("C", 44));
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback(() => _timeProvider.Advance(TimeSpan.FromMinutes(3)))
            .Returns(Task.CompletedTask);

        // Act
        var response = await HandleAsync("A", "B", "C");

        // Assert
        response.WrittenCount.Should().Be(1);
        response.FailedCount.Should().Be(0);
        response.RemainingCount.Should().Be(2);
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(43, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Flexi's own client waits five minutes for a reply, so a budget checked only between rows
    // bounds nothing: one slow write would outlive the gateway on its own. The assertion is on
    // the token the writer was handed — that it is the budget's, not the request's, is the whole
    // point, and asserting only on the counts passes just as well when it is not.
    [Fact]
    public async Task cuts_off_a_single_write_that_outlives_the_budget_instead_of_waiting_for_flexi()
    {
        // Arrange
        SetUpReports(ReportOf(
            Row("A", PriceDivergenceKind.FlexiDiffers),
            Row("B", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42), ("B", 43));

        var tokenGivenToTheWriter = CancellationToken.None;
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(42, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns((int _, decimal _, CancellationToken token) =>
            {
                // Flexi goes quiet: the budget runs out while this call is still in flight.
                tokenGivenToTheWriter = token;
                _timeProvider.Advance(TimeSpan.FromMinutes(3));

                return token.IsCancellationRequested
                    ? Task.FromCanceled(token)
                    : Task.CompletedTask;
            });

        // Act
        var response = await HandleAsync("A", "B");

        // Assert
        tokenGivenToTheWriter.IsCancellationRequested
            .Should().BeTrue("the budget has to cut off a write that is still in flight");
        response.WrittenCount.Should().Be(0);
        // Failed, not remaining: the call was made, so whether it landed is unknown.
        response.FailedCount.Should().Be(1);
        response.RemainingCount.Should().Be(1);
        _changeLog.Verify(
            l => l.AppendAsync(
                It.Is<ProductPriceChangeLog>(log => log.ProductCode == "A" && !log.FlexiSucceeded),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The append deliberately uses CancellationToken.None: a cancelled request must not lose the
    // only record of a write that did land in the live ERP.
    [Fact]
    public async Task logs_the_write_with_a_token_a_cancelled_request_cannot_take_down()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));

        // Act
        await HandleAsync("A");

        // Assert
        _changeLog.Verify(
            l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), CancellationToken.None), Times.Once);
    }

    // Stamped from the injected TimeProvider, not DateTime.UtcNow.
    [Fact]
    public async Task stamps_the_log_row_from_the_time_provider()
    {
        // Arrange
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));
        ProductPriceChangeLog? logged = null;
        _changeLog
            .Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceChangeLog, CancellationToken>((entry, _) => logged = entry)
            .Returns(Task.CompletedTask);

        // Act
        await HandleAsync("A");

        // Assert
        logged!.ChangedAt.Should().Be(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));
    }

    [Fact]
    public async Task leaves_nothing_remaining_when_the_whole_selection_was_attempted()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.RemainingCount.Should().Be(0);
    }

    // Prices have already been written by this point: propagating would report a sync that did
    // work as one that did nothing at all.
    [Fact]
    public async Task reports_the_writes_even_when_the_confirming_re_read_fails()
    {
        // Arrange
        var index = 0;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() => index++ == 0
                ? Task.FromResult(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)))
                : Task.FromException<PriceComparisonResult>(new HttpRequestException("Shoptet is down")));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.Success.Should().BeTrue();
        response.WrittenCount.Should().Be(1);
        response.Rows.Should().ContainSingle().Which.Kind.Should().Be(PriceDivergenceKind.FlexiDiffers);
    }

    [Fact]
    public async Task does_not_re_read_when_every_write_failed()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi said no"));

        // Act
        var response = await HandleAsync("A");

        // Assert
        _comparisonService.Verify(
            s => s.BuildScopedReportAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        response.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task records_every_write_in_the_price_change_log()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers, shoptet: 390m, flexi: 350m)));
        SetUpErpItems(("A", 42));
        ProductPriceChangeLog? appended = null;
        _changeLog
            .Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceChangeLog, CancellationToken>((entry, _) => appended = entry)
            .Returns(Task.CompletedTask);

        // Act
        await HandleAsync("A");

        // Assert
        appended.Should().NotBeNull();
        appended!.ProductCode.Should().Be("A");
        appended.OldPriceWithVat.Should().Be(350m);
        appended.NewPriceWithVat.Should().Be(390m);
        appended.FlexiSucceeded.Should().BeTrue();
        // True because Shoptet already holds the propagated price and was left untouched —
        // a sync never writes the shop.
        appended.ShoptetSucceeded.Should().BeTrue();
        appended.ChangedBy.Should().Be("operator@anela.cz");
        appended.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task records_a_failed_write_in_the_price_change_log()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));
        _erpWriter
            .Setup(w => w.SetPriceWithVatAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi said no"));
        ProductPriceChangeLog? appended = null;
        _changeLog
            .Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceChangeLog, CancellationToken>((entry, _) => appended = entry)
            .Returns(Task.CompletedTask);

        // Act
        await HandleAsync("A");

        // Assert
        appended.Should().NotBeNull();
        appended!.FlexiSucceeded.Should().BeFalse();
        appended.ErrorMessage.Should().Contain("Flexi said no");
    }

    // The log is history; it must never be able to turn a completed live ERP write into a
    // reported failure.
    [Fact]
    public async Task completes_the_sync_even_when_the_change_log_insert_fails()
    {
        // Arrange
        SetUpReports(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
        SetUpErpItems(("A", 42));
        _changeLog
            .Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.WrittenCount.Should().Be(1);
        response.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task passes_the_requested_codes_through_to_the_comparison_service()
    {
        // Arrange
        IReadOnlyCollection<string>? received = null;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, CancellationToken>((codes, _) => received = codes)
            .ReturnsAsync(new PriceComparisonResult());

        // Act
        await HandleAsync("A", "B");

        // Assert
        received.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    // A read that times out throws TaskCanceledException — an OperationCanceledException — while
    // this request's own token stays uncancelled. Filtering the re-read's catch on the exception
    // TYPE would let that through and report a sync whose prices did reach the ERP as a failure.
    [Fact]
    public async Task reports_the_writes_when_the_confirming_re_read_times_out_flexi_side()
    {
        // Arrange
        var index = 0;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() => index++ == 0
                ? Task.FromResult(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)))
                : Task.FromException<PriceComparisonResult>(
                    new TaskCanceledException("Flexi timed out", new TimeoutException())));
        SetUpErpItems(("A", 42));

        // Act
        var response = await HandleAsync("A");

        // Assert
        response.WrittenCount.Should().Be(1);
        response.Rows.Should().ContainSingle(r => r.ProductCode == "A");
    }

    // The counterpart: the operator (or the gateway) actually walking away must still abort.
    [Fact]
    public async Task propagates_cancellation_when_the_caller_walks_away_during_the_re_read()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var index = 0;
        _comparisonService
            .Setup(s => s.BuildScopedReportAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (index++ == 0)
                {
                    return Task.FromResult(ReportOf(Row("A", PriceDivergenceKind.FlexiDiffers)));
                }

                cts.Cancel();
                return Task.FromCanceled<PriceComparisonResult>(cts.Token);
            });
        SetUpErpItems(("A", 42));

        // Act
        var act = async () => await CreateHandler().Handle(
            new SyncProductPricesRequest { ProductCodes = new List<string> { "A" } }, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
