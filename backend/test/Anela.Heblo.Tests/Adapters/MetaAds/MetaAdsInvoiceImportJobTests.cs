using Anela.Heblo.Adapters.MetaAds;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.MetaAds;

public class MetaAdsInvoiceImportJobTests
{
    private const string JobName = "meta-ads-invoice-import";

    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();

    private MetaAdsInvoiceImportJob CreateJob() =>
        new(_mockMediator.Object, _mockStatusChecker.Object, NullLogger<MetaAdsInvoiceImportJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotDispatch()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportMarketingInvoicesResponse());

        await CreateJob().ExecuteAsync(default);

        _mockMediator.Verify(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_DispatchesMetaAdsRequestWithSevenDayWindow()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportMarketingInvoicesResponse { Platform = "MetaAds", Imported = 3 });

        await CreateJob().ExecuteAsync(default);

        _mockMediator.Verify(
            m => m.Send(
                It.Is<ImportMarketingInvoicesRequest>(req =>
                    req.Platform == MetaAdsTransactionSource.PlatformName &&
                    req.To - req.From == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchThrows_ExceptionIsRethrown()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Meta API down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateJob().ExecuteAsync(default));
    }
}
