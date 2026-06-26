using Anela.Heblo.Application.Features.MarketingInvoices;
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class ImportMarketingInvoicesHandlerTests
{
    private readonly Mock<IMarketingInvoiceImportService> _mockImportService = new();

    private ImportMarketingInvoicesHandler CreateHandler(IEnumerable<IMarketingTransactionSource> sources) =>
        new(sources, _mockImportService.Object, NullLogger<ImportMarketingInvoicesHandler>.Instance);

    private static Mock<IMarketingTransactionSource> SourceFor(string platform)
    {
        var mock = new Mock<IMarketingTransactionSource>();
        mock.Setup(s => s.Platform).Returns(platform);
        return mock;
    }

    [Fact]
    public async Task Handle_SelectsSourceMatchingPlatform_AndMapsResult()
    {
        // Arrange
        var from = new DateTime(2026, 5, 1);
        var to = new DateTime(2026, 5, 8);

        var meta = SourceFor("MetaAds");
        var google = SourceFor("GoogleAds");

        _mockImportService
            .Setup(s => s.ImportAsync(meta.Object, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketingImportResult { Imported = 1, Skipped = 0, Failed = 0 });

        var handler = CreateHandler(new[] { meta.Object, google.Object });

        // Act
        var response = await handler.Handle(
            new ImportMarketingInvoicesRequest { Platform = "MetaAds", From = from, To = to },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("MetaAds", response.Platform);
        Assert.Equal(1, response.Imported);
        Assert.Equal(0, response.Skipped);
        Assert.Equal(0, response.Failed);
        _mockImportService.Verify(
            s => s.ImportAsync(google.Object, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownPlatform_ThrowsArgumentException()
    {
        var handler = CreateHandler(new[] { SourceFor("MetaAds").Object });

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "TikTokAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_DuplicatePlatform_ThrowsInvalidOperationException()
    {
        var handler = CreateHandler(new[] { SourceFor("MetaAds").Object, SourceFor("MetaAds").Object });

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "MetaAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SourceThrows_ExceptionPropagates()
    {
        var meta = SourceFor("MetaAds");

        _mockImportService
            .Setup(s => s.ImportAsync(meta.Object, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Meta API down"));

        var handler = CreateHandler(new[] { meta.Object });

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "MetaAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }
}
