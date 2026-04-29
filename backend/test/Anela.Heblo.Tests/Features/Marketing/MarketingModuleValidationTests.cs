using System;
using System.Collections.Generic;
using Anela.Heblo.Application.Features.Marketing;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public sealed class MarketingModuleValidationTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static MarketingCalendarOptions BuildOptions(
        Dictionary<string, MarketingActionType>? categoryMappings = null,
        Dictionary<MarketingActionType, string>? outgoingCategories = null)
    {
        return new MarketingCalendarOptions
        {
            CategoryMappings = categoryMappings
                ?? new Dictionary<string, MarketingActionType>(System.StringComparer.OrdinalIgnoreCase),
            OutgoingCategories = outgoingCategories
                ?? new Dictionary<MarketingActionType, string>(),
        };
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_BothDictionariesEmpty_Passes()
    {
        // Arrange
        var options = BuildOptions();

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OnlyCategoryMappingsPopulated_Passes()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["Sociální sítě"] = MarketingActionType.General,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>());

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RoundTripValid_Passes()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["Sociální sítě"] = MarketingActionType.General,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.General] = "Sociální sítě",
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_OutgoingValueMissingFromIncoming_Throws()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["A"] = MarketingActionType.General,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.Campaign] = "B",
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*OutgoingCategories[Campaign] = 'B'*");
    }

    [Fact]
    public void Validate_CaseInsensitiveMatch_Passes()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["Sociální Sítě"] = MarketingActionType.General,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.General] = "sociální sítě",
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_TrimmedMatch_Passes()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["Email"] = MarketingActionType.Launch,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.Launch] = "  Email  ",
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MultipleErrors_AllListedInMessage()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["ValidKey"] = MarketingActionType.General,
            },
            outgoingCategories: new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.Campaign] = "MissingX",
                [MarketingActionType.Launch] = "MissingY",
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("OutgoingCategories[Campaign]")
            .And.Contain("OutgoingCategories[Launch]");
    }
}
