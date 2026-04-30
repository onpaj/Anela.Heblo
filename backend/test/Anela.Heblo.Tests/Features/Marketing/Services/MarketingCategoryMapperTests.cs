using System;
using System.Collections.Generic;
using System.Text;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Features.Marketing.Services;

public sealed class MarketingCategoryMapperTests
{
    // ---------------------------------------------------------------------------
    // Helper: minimal IOptionsMonitor<T> implementation for tests
    // ---------------------------------------------------------------------------

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private T _current;
        private readonly List<Action<T, string?>> _listeners = new();

        public TestOptionsMonitor(T initial)
        {
            _current = initial;
        }

        public T CurrentValue => _current;

        public T Get(string? name) => _current;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void Set(T next)
        {
            _current = next;
            foreach (var l in _listeners.ToArray())
            {
                l(next, null);
            }
        }

        public void SetNull()
        {
            foreach (var l in _listeners.ToArray())
            {
                l(default(T)!, null);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;

            public Subscription(Action d)
            {
                _dispose = d;
            }

            public void Dispose() => _dispose();
        }
    }

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
        result.ActionType.Should().Be(MarketingActionType.General);
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
        result.ActionType.Should().Be(MarketingActionType.General);
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
        result.ActionType.Should().Be(MarketingActionType.General);
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
                ["PR – léto"] = MarketingActionType.Campaign,
                ["Sociální sítě"] = MarketingActionType.General,
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToActionType(new[] { "Random", "PR – léto", "Sociální sítě" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.Campaign);
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
        result.ActionType.Should().Be(MarketingActionType.General);
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
                ["Sociální sítě"] = MarketingActionType.General,
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToActionType(new[] { "sociální SÍTĚ" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.General);
        result.MatchedCategory.Should().Be("sociální SÍTĚ");
        result.UnmappedCategories.Should().BeEmpty();
    }

    [Fact]
    public void MapToOutlookCategory_KnownActionType_ReturnsConfiguredName()
    {
        // Arrange
        var opts = new MarketingCalendarOptions
        {
            GroupId = "g",
            OutgoingCategories = new Dictionary<MarketingActionType, string>
            {
                [MarketingActionType.Campaign] = "PR – léto",
            },
        };
        var mapper = CreateMapper(opts);

        // Act
        var result = mapper.MapToOutlookCategory(MarketingActionType.Campaign);

        // Assert
        result.Should().Be("PR – léto");
    }

    [Fact]
    public void MapToOutlookCategory_UnknownActionType_FallsBackToToString()
    {
        // Arrange
        var mapper = CreateMapper(EmptyOptions());

        // Act
        var result = mapper.MapToOutlookCategory(MarketingActionType.Campaign);

        // Assert
        result.Should().Be("Campaign");
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
                ["X"] = MarketingActionType.Promotion,
            },
        });

        var result = mapper.MapToActionType(new[] { "X" });

        // Assert
        result.ActionType.Should().Be(MarketingActionType.Promotion);
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
                ["X"] = MarketingActionType.Promotion,
            },
        };

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(initialOpts);
        var mapper = new MarketingCategoryMapper(monitor, NullLogger<MarketingCategoryMapper>.Instance);

        // Verify initial mapping works
        mapper.MapToActionType(new[] { "X" }).ActionType.Should().Be(MarketingActionType.Promotion);

        // Act – trigger OnChange with null to force NullReferenceException inside BuildSnapshot
        // (null opts causes NRE when accessing opts.CategoryMappings)
        monitor.SetNull();

        // Assert – prior snapshot is retained
        var result = mapper.MapToActionType(new[] { "X" });
        result.ActionType.Should().Be(MarketingActionType.Promotion);
        result.MatchedCategory.Should().Be("X");
    }

    [Fact]
    public void BinderProducedDictionary_WithoutComparer_StillResolvesCaseInsensitively()
    {
        // Arrange – simulate options bound from JSON (no OrdinalIgnoreCase comparer on CategoryMappings)
        const string json =
            """{"MarketingCalendar":{"GroupId":"g","PushEnabled":true,"CategoryMappings":{"Sociální sítě":"General"},"OutgoingCategories":{}}}""";

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
        result.ActionType.Should().Be(MarketingActionType.General);
        result.MatchedCategory.Should().Be("sociální sítě");
        result.UnmappedCategories.Should().BeEmpty();
    }

}
