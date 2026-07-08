using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class GetFinancialComparisonHandlerTests
{
    private readonly Mock<IFinancialAnalysisService> _serviceMock = new();

    [Fact]
    public async Task Handle_PassesRequestValuesToService_AndDefaultsYearsTo3()
    {
        // Arrange
        var expected = new GetFinancialComparisonResponse();
        _serviceMock
            .Setup(x => x.GetFinancialComparisonAsync(
                3, true, It.IsAny<IReadOnlyList<string>?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new GetFinancialComparisonHandler(
            _serviceMock.Object, NullLogger<GetFinancialComparisonHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new GetFinancialComparisonRequest { Years = null }, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _serviceMock.Verify(x => x.GetFinancialComparisonAsync(
            3, true, It.IsAny<IReadOnlyList<string>?>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
