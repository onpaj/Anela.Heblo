using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductMarginsHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetProductMarginsHandler _handler;

    public GetProductMarginsHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetProductMarginsHandler>>();
        _handler = new GetProductMarginsHandler(
            _catalogRepositoryMock.Object,
            _timeProviderMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow()
    {
        // Arrange
        var utcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var expectedDateFrom = utcNow.AddMonths(-13); // 2025-05-15T12:00:00
        _timeProviderMock
            .Setup(tp => tp.GetUtcNow())
            .Returns(new DateTimeOffset(utcNow, TimeSpan.Zero));

        var atBoundaryKey = expectedDateFrom;                      // included (>=)
        var justBeforeBoundaryKey = expectedDateFrom.AddTicks(-1); // excluded
        var wellWithinKey = new DateTime(2026, 1, 1);              // included

        var aggregate = BuildAggregate(
            productCode: "TEST001",
            monthlyKeys: new[] { atBoundaryKey, justBeforeBoundaryKey, wellWithinKey });

        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { aggregate });

        var request = new GetProductMarginsRequest();

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(1);

        var monthlyHistory = response.Items[0].MonthlyHistory;
        monthlyHistory.Select(m => m.Month).Should().BeEquivalentTo(new[] { atBoundaryKey, wellWithinKey });
        monthlyHistory.Select(m => m.Month).Should().NotContain(justBeforeBoundaryKey);
    }

    [Fact]
    public async Task Handle_UsesUtcNotLocalTime_AtDayBoundary()
    {
        // Arrange — UTC time 2025-12-31T23:30:00Z. In a UTC+1 zone this is 2026-01-01T00:30:00.
        // Correct (UTC) dateFrom = 2024-11-30T23:30:00.
        // Bug-mode (local-time) dateFrom would be 2024-12-01T00:30:00 — one month later.
        // An entry keyed at 2024-12-01T00:00:00 is >= the UTC dateFrom but < the local-time dateFrom.
        // Asserting it is INCLUDED demonstrates the new code uses UTC.
        var utcNow = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        _timeProviderMock
            .Setup(tp => tp.GetUtcNow())
            .Returns(new DateTimeOffset(utcNow, TimeSpan.Zero));

        var discriminatingKey = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        // Included under UTC semantics (2024-11-30T23:30 <= 2024-12-01T00:00),
        // excluded under local-time UTC+1 semantics (2024-12-01T00:30 > 2024-12-01T00:00).

        var aggregate = BuildAggregate(
            productCode: "TEST002",
            monthlyKeys: new[] { discriminatingKey });

        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { aggregate });

        var request = new GetProductMarginsRequest();

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(1);
        response.Items[0].MonthlyHistory.Select(m => m.Month)
            .Should().ContainSingle()
            .Which.Should().Be(discriminatingKey);
    }

    private static CatalogAggregate BuildAggregate(string productCode, IEnumerable<DateTime> monthlyKeys)
    {
        var aggregate = new CatalogAggregate
        {
            Id = productCode,
            ProductName = "Test Product",
            Type = ProductType.Product
        };

        foreach (var key in monthlyKeys)
        {
            aggregate.Margins.MonthlyData[key] = new MarginData();
        }

        return aggregate;
    }
}
