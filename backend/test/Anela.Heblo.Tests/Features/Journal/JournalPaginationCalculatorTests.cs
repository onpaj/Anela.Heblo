using Anela.Heblo.Application.Features.Journal.Pagination;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class JournalPaginationCalculatorTests
{
    [Theory]
    [InlineData(0, 1, 10, 0, false, false)]      // no rows at all
    [InlineData(5, 1, 10, 1, false, false)]       // single partial page
    [InlineData(10, 1, 10, 1, false, false)]      // exact multiple, on last page
    [InlineData(11, 1, 10, 2, true, false)]       // exact multiple + 1, more pages follow
    [InlineData(11, 2, 10, 2, false, true)]       // second (last) page of the above
    [InlineData(25, 2, 10, 3, true, true)]        // middle page
    [InlineData(25, 3, 10, 3, false, true)]       // last page, remainder row
    public void Calculate_ReturnsExpectedPaginationMetadata(
        int totalCount, int pageNumber, int pageSize,
        int expectedTotalPages, bool expectedHasNextPage, bool expectedHasPreviousPage)
    {
        // Act
        var result = JournalPaginationCalculator.Calculate(totalCount, pageNumber, pageSize);

        // Assert
        result.TotalPages.Should().Be(expectedTotalPages);
        result.HasNextPage.Should().Be(expectedHasNextPage);
        result.HasPreviousPage.Should().Be(expectedHasPreviousPage);
    }
}
