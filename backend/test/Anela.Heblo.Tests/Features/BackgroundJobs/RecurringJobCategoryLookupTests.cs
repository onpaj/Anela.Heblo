using Anela.Heblo.Application.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobCategoryLookupTests
{
    [Fact]
    public void Resolve_ReturnsCategoryOfDiscoveredJob()
    {
        // Arrange
        var lookup = RecurringJobCategoryLookup.Build(new IRecurringJob[]
        {
            new FakeRecurringJob("invoice-import", RecurringJobCategory.Finance),
            new FakeRecurringJob("photobank-index", RecurringJobCategory.Content)
        });

        // Act
        var category = RecurringJobCategoryLookup.Resolve(lookup, "photobank-index");

        // Assert
        category.Should().Be(RecurringJobCategory.Content);
    }

    [Fact]
    public void Resolve_ReturnsUncategorized_WhenJobHasNoImplementation()
    {
        // Arrange
        var lookup = RecurringJobCategoryLookup.Build(new IRecurringJob[]
        {
            new FakeRecurringJob("invoice-import", RecurringJobCategory.Finance)
        });

        // Act
        var category = RecurringJobCategoryLookup.Resolve(lookup, "removed-job");

        // Assert
        category.Should().Be(RecurringJobCategory.Uncategorized);
    }

    [Fact]
    public void Resolve_MatchesJobNameCaseSensitively_LikeTheRestOfTheFeature()
    {
        // Arrange
        var lookup = RecurringJobCategoryLookup.Build(new IRecurringJob[]
        {
            new FakeRecurringJob("invoice-import", RecurringJobCategory.Finance)
        });

        // Act
        var category = RecurringJobCategoryLookup.Resolve(lookup, "Invoice-Import");

        // Assert
        category.Should().Be(RecurringJobCategory.Uncategorized);
    }

    [Fact]
    public void Build_KeepsTheFirstRegistration_WhenJobNameIsDuplicated()
    {
        // Arrange
        var jobs = new IRecurringJob[]
        {
            new FakeRecurringJob("invoice-import", RecurringJobCategory.Finance),
            new FakeRecurringJob("invoice-import", RecurringJobCategory.Marketing)
        };

        // Act
        var lookup = RecurringJobCategoryLookup.Build(jobs);

        // Assert
        lookup.Should().ContainSingle();
        RecurringJobCategoryLookup.Resolve(lookup, "invoice-import").Should().Be(RecurringJobCategory.Finance);
    }

    [Fact]
    public void Build_Throws_WhenDiscoveredJobsIsNull()
    {
        // Act
        var act = () => RecurringJobCategoryLookup.Build(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
