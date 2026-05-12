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
        Dictionary<string, MarketingActionType>? categoryMappings = null)
    {
        return new MarketingCalendarOptions
        {
            CategoryMappings = categoryMappings
                ?? new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase),
        };
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyMappings_Passes()
    {
        // Arrange
        var options = BuildOptions();

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidMappings_Passes()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["Sociální sítě"] = MarketingActionType.SocialMedia,
                ["Email"] = MarketingActionType.Newsletter,
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_BlankKey_Throws()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                [""] = MarketingActionType.SocialMedia,
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CategoryMappings contains a blank key*");
    }

    [Fact]
    public void Validate_WhitespaceOnlyKey_Throws()
    {
        // Arrange
        var options = BuildOptions(
            categoryMappings: new Dictionary<string, MarketingActionType>
            {
                ["   "] = MarketingActionType.SocialMedia,
            });

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*CategoryMappings contains a blank key*");
    }

    [Fact]
    public void Validate_NullMappings_Passes()
    {
        // Arrange
        var options = new MarketingCalendarOptions { CategoryMappings = null! };

        // Act
        var act = () => MarketingModule.ValidateRoundTrip(options);

        // Assert
        act.Should().NotThrow();
    }
}
