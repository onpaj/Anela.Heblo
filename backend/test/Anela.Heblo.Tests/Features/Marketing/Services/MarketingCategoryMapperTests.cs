using System;
using System.Collections.Generic;
using System.Text;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Features.Marketing.Services;

public sealed class MarketingCategoryMapperTests
{
    // ---------------------------------------------------------------------------
    // Factory helpers
    // ---------------------------------------------------------------------------

    private static MarketingCalendarOptions EmptyOptions() =>
        new MarketingCalendarOptions
        {
            GroupId = "g",
            PushEnabled = false,
        };

    private static MarketingCategoryMapper CreateMapper(MarketingCalendarOptions opts)
    {
        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(opts);
        return new MarketingCategoryMapper(monitor, NullLogger<MarketingCategoryMapper>.Instance);
    }

    private static (MarketingCategoryMapper Mapper, TestOptionsMonitor<MarketingCalendarOptions> Monitor) CreateMapperWithMonitor(
        MarketingCalendarOptions opts)
    {
        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(opts);
        var mapper = new MarketingCategoryMapper(monitor, NullLogger<MarketingCategoryMapper>.Instance);
        return (mapper, monitor);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void MapToActionType_EmptyList_ReturnsGeneralWithNoUnmapped()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToActionType(Array.Empty<string>());

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().BeNull();
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToActionType_NullList_ReturnsGeneralWithNoUnmapped()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToActionType((IReadOnlyList<string>)null!);

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().BeNull();
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToActionType_AllWhitespace_ReturnsGeneralWithNoUnmapped()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToActionType(new[] { "", " ", "\t" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().BeNull();
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToActionType_FirstMappedWins()
    {
        // Arrange
        var opts = new MarketingCalendarOptions
        {
            GroupId = "g",
            CategoryMappings = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["PR – léto"] = MarketingActionType.PR,
                ["Sociální sítě"] = MarketingActionType.SocialMedia,
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToActionType(new[] { "Random", "PR – léto", "Sociální sítě" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.PR);
        result.MatchedCategory.Should().Be("PR – léto");
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToActionType_NoMappings_ReturnsGeneralAndUnmappedListsAllNonWhitespace()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToActionType(new[] { "Random", " ", "Another" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().BeNull();
        result.UnmappedCategories.Should().BeEquivalentTo(new[] { "Random", "Another" });
    }

    [Fact]
    public void MapToActionType_CaseInsensitive()
    {
        // Arrange
        var opts = new MarketingCalendarOptions
        {
            GroupId = "g",
            CategoryMappings = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sociální sítě"] = MarketingActionType.SocialMedia,
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToActionType(new[] { "sociální SÍTĚ" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().Be("sociální SÍTĚ");
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToOutlookCategory_KnownActionType_ReturnsFirstMappedOutlookCategory()
    {
        // Arrange
        var opts = new MarketingCalendarOptions
        {
            GroupId = "g",
            CategoryMappings = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["PR – léto"] = MarketingActionType.PR,
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToOutlookCategory(MarketingActionType.PR);

        // Assert
        result.Should().Be("PR – léto");
    }

    [Fact]
    public void MapToOutlookCategory_UnknownActionType_FallsBackToToString()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToOutlookCategory(MarketingActionType.PR);

        // Assert
        result.Should().Be("PR");
    }

    [Fact]
    public void OnChange_RebuildsSnapshot_NewMappingTakesEffect()
    {
        // Arrange
        var (mapper, monitor) = CreateMapperWithMonitor(EmptyOptions());

        // Act – trigger hot reload with new mapping
        monitor.Set(new MarketingCalendarOptions
        {
            GroupId = "g",
            CategoryMappings = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = MarketingActionType.Blog,
            },
        });

        var result = mapper.MapToActionType(new[] { "X" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.Blog);
        result.MatchedCategory.Should().Be("X");
    }

    [Fact]
    public void OnChange_FailureRetainsPriorSnapshot()
    {
        // Arrange – initial config maps "X" -> Promotion
        var initialOpts = new MarketingCalendarOptions
        {
            GroupId = "g",
            CategoryMappings = new Dictionary<string, MarketingActionType>(StringComparer.OrdinalIgnoreCase)
            {
                ["X"] = MarketingActionType.Blog,
            },
        };

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(initialOpts);
        var mapper = new MarketingCategoryMapper(monitor, NullLogger<MarketingCategoryMapper>.Instance);

        // Verify initial mapping works
        mapper.MapToActionType(new[] { "X" }).ActionType.Should().Be(MarketingActionType.Blog);

        // Act – trigger OnChange with null to force NullReferenceException inside BuildSnapshot
        // (null opts causes NRE when accessing opts.CategoryMappings)
        monitor.SetNull();

        // Assert – prior snapshot is retained
        var result = mapper.MapToActionType(new[] { "X" });
        result.ActionType.Should().Be(MarketingActionType.Blog);
        result.MatchedCategory.Should().Be("X");
    }

    [Fact]
    public void BinderProducedDictionary_WithoutComparer_StillResolvesCaseInsensitively()
    {
        // Arrange – simulate options bound from JSON (no OrdinalIgnoreCase comparer on CategoryMappings)
        const string json =
            """{"MarketingCalendar":{"GroupId":"g","PushEnabled":true,"CategoryMappings":{"Sociální sítě":"SocialMedia"}}}""";

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        var boundOpts = configuration.GetSection("MarketingCalendar").Get<MarketingCalendarOptions>()
            ?? new MarketingCalendarOptions();

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(boundOpts);
        var mapper = new MarketingCategoryMapper(monitor, NullLogger<MarketingCategoryMapper>.Instance);

        // Act
        var result = mapper.MapToActionType(new[] { "sociální sítě" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.SocialMedia);
        result.MatchedCategory.Should().Be("sociální sítě");
        result.UnmappedCategories.Should().BeEmpty();
    }

}
