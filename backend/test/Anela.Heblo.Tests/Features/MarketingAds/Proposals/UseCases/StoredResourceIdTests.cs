using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class StoredResourceIdTests
{
    [Theory]
    [InlineData("""{"platformResourceId":"customers/1/campaignCriteria/5~7","platformResponse":{"results":[{"resourceName":"other"}]}}""", "customers/1/campaignCriteria/5~7")]
    [InlineData("""{"results":[{"resourceName":"customers/1/adGroupCriteria/5~7"}]}""", "customers/1/adGroupCriteria/5~7")]
    [InlineData("""{"campaignCriterion":{"resourceName":"customers/1/campaignCriteria/5~7","keyword":{"text":"x"}}}""", "customers/1/campaignCriteria/5~7")]
    [InlineData("""{"results":[{"resourceName":"a"},{"resourceName":"a"}]}""", "a")]
    public void Recover_ReturnsTheSingleNamedResource(string json, string expected)
    {
        StoredResourceId.Recover(json).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"results":[{"resourceName":"a"},{"resourceName":"b"}]}""")]
    [InlineData("""{"resourceName":42}""")]
    public void Recover_ReturnsNull_WhenNoSingleResourceIsNamed(string? json)
    {
        StoredResourceId.Recover(json).Should().BeNull();
    }
}
