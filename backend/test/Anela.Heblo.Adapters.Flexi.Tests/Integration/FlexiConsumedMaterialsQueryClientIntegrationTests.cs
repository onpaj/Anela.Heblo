using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[Collection("FlexiIntegration")]
public class FlexiConsumedMaterialsQueryClientIntegrationTests : IClassFixture<FlexiIntegrationTestFixture>
{
    private readonly FlexiIntegrationTestFixture _fixture;
    private readonly IConsumedMaterialsClient _client;

    private static DateTime ReferenceTime = DateTime.Parse("2025-07-01");

    public FlexiConsumedMaterialsQueryClientIntegrationTests(FlexiIntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.ServiceProvider.GetRequiredService<IConsumedMaterialsClient>();
    }

    [Fact]
    public async Task GetConsumedAsync_WithValidDateRange_ReturnsConsumedMaterials()
    {
        // Arrange
        var dateFrom = ReferenceTime;
        var dateTo = dateFrom.AddDays(30);
        var limit = 10;

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<ConsumedMaterialRecord>>();

        if (result.Any())
        {
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.ProductCode));
            result.Should().OnlyContain(record => !string.IsNullOrWhiteSpace(record.ProductName));
            result.Should().OnlyContain(record => record.Amount > 0);
            result.Should().OnlyContain(record => record.Date >= dateFrom && record.Date <= dateTo);

            // Test limit parameter
            result.Count.Should().BeLessOrEqualTo(limit);
        }
    }

    [Fact]
    public async Task GetConsumedAsync_WithLimitZero_ReturnsAllRecords()
    {
        // Arrange
        var dateFrom = ReferenceTime;
        var dateTo = dateFrom.AddDays(30);
        var limit = 0; // No limit

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<ConsumedMaterialRecord>>();

        if (result.Any())
        {
            result.Should().OnlyContain(record => record.Date >= dateFrom && record.Date <= dateTo);
        }
    }

    [Fact]
    public async Task GetConsumedAsync_WithVeryRecentDateRange_MayReturnEmptyResults()
    {
        // Arrange
        var dateFrom = ReferenceTime;
        var dateTo = dateFrom.AddMinutes(1);

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<ConsumedMaterialRecord>>();
        // May be empty, which is valid for such a short time range
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task GetConsumedAsync_WithDifferentLimits_RespectsLimitParameter(int limit)
    {
        // Arrange
        var dateFrom = ReferenceTime;
        var dateTo = dateFrom.AddDays(30);

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Count.Should().BeLessOrEqualTo(limit);
    }

    [Fact]
    public async Task GetConsumedAsync_WithLongDateRange_ReturnsValidData()
    {
        // Arrange
        var dateFrom = ReferenceTime.AddDays(-365); // Last year
        var dateTo = ReferenceTime;
        var limit = 20;

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo, limit);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IReadOnlyList<ConsumedMaterialRecord>>();

        if (result.Any())
        {
            // Verify data structure
            foreach (var record in result)
            {
                record.ProductCode.Should().NotBeNullOrWhiteSpace();
                record.ProductName.Should().NotBeNullOrWhiteSpace();
                record.Amount.Should().BeGreaterThan(0);
                record.Date.Should().BeAfter(dateFrom.AddDays(-1)); // Allow small tolerance
                record.Date.Should().BeBefore(dateTo.AddDays(1)); // Allow small tolerance
            }

            // Check that results are within limit
            result.Count.Should().BeLessOrEqualTo(limit);
        }
    }

    [Fact]
    public async Task GetConsumedAsync_WithFutureDateRange_ReturnsEmptyResults()
    {
        // Arrange
        var dateFrom = DateTime.Now.AddDays(1); // Tomorrow
        var dateTo = DateTime.Now.AddDays(7); // Next week

        // Act
        var result = await _client.GetConsumedAsync(dateFrom, dateTo);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty("because future dates should not have consumed materials data");
    }

    [Fact]
    public async Task GetConsumedAsync_WithReversedDateRange_ThrowsExceptionOrReturnsEmpty()
    {
        // Arrange
        var dateFrom = ReferenceTime; // Later date
        var dateTo = ReferenceTime.AddDays(-30); // Earlier date

        // Act
        var act = async () => await _client.GetConsumedAsync(dateFrom, dateTo);

        // Assert
        // Either throws an exception or returns empty results
        // The exact behavior depends on FlexiBee's implementation
        var result = await act.Should().NotThrowAsync();
        if (result.Subject != null)
        {
            result.Subject.Should().BeEmpty("because reversed date range should not return data");
        }
    }

    [Fact]
    public async Task Integration_ConsumedMaterialsWorkflow_ValidatesDataConsistency()
    {
        // This test validates the complete workflow and data consistency

        // Step 1: Get consumed materials for last month
        var dateFrom = ReferenceTime.AddDays(-30);
        var dateTo = ReferenceTime;

        var allRecords = await _client.GetConsumedAsync(dateFrom, dateTo, 0); // No limit
        var limitedRecords = await _client.GetConsumedAsync(dateFrom, dateTo, 5); // With limit

        // Step 2: Validate consistency
        allRecords.Should().NotBeNull();
        limitedRecords.Should().NotBeNull();

        if (allRecords.Any())
        {
            // Limited results should be a subset of all results
            limitedRecords.Count.Should().BeLessOrEqualTo(Math.Min(5, allRecords.Count));

            // All limited records should exist in the full set
            foreach (var limitedRecord in limitedRecords)
            {
                allRecords.Should().Contain(r =>
                    r.ProductCode == limitedRecord.ProductCode &&
                    r.Date == limitedRecord.Date &&
                    Math.Abs(r.Amount - limitedRecord.Amount) < 0.001);
            }
        }

        // Step 3: Validate data types and ranges
        foreach (var record in allRecords.Take(10)) // Test first 10 records
        {
            record.ProductCode.Should().NotBeNullOrWhiteSpace();
            record.ProductName.Should().NotBeNullOrWhiteSpace();
            record.Amount.Should().BeGreaterThan(0);
            record.Date.Should().BeAfter(dateFrom.AddDays(-1));
            record.Date.Should().BeBefore(dateTo.AddDays(1));
        }
    }
}