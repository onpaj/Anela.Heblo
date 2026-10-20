using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.MarketingAds;

public class AdPagingTests
{
    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(3, 50, 100)]
    [InlineData(0, 50, 0)]
    [InlineData(-5, 50, 0)]
    [InlineData(int.MaxValue, 200, int.MaxValue)]
    public void Skip_IsNeverNegative_AndCapsInsteadOfWrapping(int page, int size, int expected)
    {
        AdPaging.Skip(page, size).Should().Be(expected);
    }
}
