using System.Globalization;
using Anela.Heblo.Domain.Features.Attendance;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Attendance;

public class IntegrationNoteTests
{
    private const string Marker = "integration";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("somebody else")]
    [InlineData("integrationX")]      // marker must be followed by whitespace or nothing
    [InlineData("not integration")]   // marker must start the note
    public void Parse_NotEnrolled_WhenMarkerIsAbsent(string? note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeFalse();
        result.DailyHours.Should().BeNull();
    }

    [Theory]
    [InlineData("integration")]
    [InlineData("  integration  ")]
    [InlineData("INTEGRATION")]
    public void Parse_EnrolledWithoutHours_WhenNoteIsMarkerOnly(string note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().BeNull();
    }

    [Theory]
    [InlineData("integration 6,4", 6, 24)]
    [InlineData("integration 6.4", 6, 24)]
    [InlineData("integration 8", 8, 0)]
    [InlineData("integration   7,5", 7, 30)]
    [InlineData("Integration 6,4", 6, 24)]
    public void Parse_ReadsDailyHours(string note, int expectedHours, int expectedMinutes)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().Be(new TimeSpan(expectedHours, expectedMinutes, 0));
    }

    [Theory]
    [InlineData("integration abc")]
    [InlineData("integration 0")]
    [InlineData("integration -3")]
    [InlineData("integration 25")]
    [InlineData("integration 6,4 extra")]
    public void Parse_EnrolledWithoutHours_WhenHoursAreUnusable(string note)
    {
        var result = IntegrationNote.Parse(note, Marker);

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().BeNull();
    }

    [Fact]
    public void Parse_UsesDefaultMarker_WhenMarkerIsOmitted()
    {
        // The overtime provider has no per-job NoteMarker option of its own.
        var result = IntegrationNote.Parse("integration 6,4");

        result.IsEnrolled.Should().BeTrue();
        result.DailyHours.Should().Be(new TimeSpan(6, 24, 0));
        IntegrationNote.DefaultMarker.Should().Be("integration");
    }

    [Fact]
    public void Parse_IsCultureInvariant()
    {
        // A culture whose decimal separator is "," must not change how "6.4" parses,
        // and vice versa. Without invariant parsing this test flips on a Czech server.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            IntegrationNote.Parse("integration 6.4", Marker).DailyHours
                .Should().Be(new TimeSpan(6, 24, 0));
            IntegrationNote.Parse("integration 6,4", Marker).DailyHours
                .Should().Be(new TimeSpan(6, 24, 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
