using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceDivergenceReport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class GetPriceDivergenceReportHandlerTests
{
    [Fact]
    public async Task returns_rows_and_summary_from_the_report_service_unchanged()
    {
        // Arrange
        var reportService = new Mock<IPriceDivergenceReportService>();
        var report = new PriceDivergenceReportResult
        {
            Rows = new List<PriceDivergenceRowDto>
            {
                new() { ProductCode = "A", Kind = PriceDivergenceKind.InAgreement },
            },
            Summary = new PriceDivergenceSummaryDto { TotalInScope = 1, InAgreementCount = 1 },
        };
        reportService
            .Setup(s => s.BuildReportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        var handler = new GetPriceDivergenceReportHandler(reportService.Object);

        // Act
        var response = await handler.Handle(new GetPriceDivergenceReportRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Rows.Should().BeEquivalentTo(report.Rows);
        response.Summary.Should().BeEquivalentTo(report.Summary);
    }
}
