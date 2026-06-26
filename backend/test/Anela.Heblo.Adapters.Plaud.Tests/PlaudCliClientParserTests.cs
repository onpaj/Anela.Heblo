using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientParserTests
{
    [Fact]
    public async Task ParseFilesOutput_WithFixture_ParsesAllRecordings()
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_recent_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        var result = PlaudCliClient.ParseFilesOutput(fixtureContent);

        result.Should().HaveCount(3);

        result[0].Id.Should().Be("3465d31a29dce3a3819a7d164f86b12c");
        result[0].Name.Should().BeEmpty();
        result[0].CreatedAt.Should().Be(new DateTime(2026, 5, 10));

        result[1].Id.Should().Be("f3aca6bf803532f53e6ae53c3b0b7495");
        result[1].Name.Should().BeEmpty();
        result[1].CreatedAt.Should().Be(new DateTime(2026, 5, 9));

        result[2].Id.Should().Be("b6c774e4c9b2c55fa8159db2430726cc");
        result[2].Name.Should().Be("05-13 Weekly Team Standup");
        result[2].CreatedAt.Should().Be(new DateTime(2026, 5, 8));
    }

    [Fact]
    public void ParseFilesOutput_WithEmptyInput_ReturnsEmptyList()
    {
        PlaudCliClient.ParseFilesOutput(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_WithHeaderOnly_ReturnsEmptyList()
    {
        PlaudCliClient.ParseFilesOutput("Recordings in the last 7 days: 0").Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_IgnoresLinesWithInvalidId()
    {
        var input = """
            Recordings in the last 7 days: 1

              not-a-valid-id  2026-05-10  1h00m
              b6c774e4c9b2c55fa8159db2430726cc  2026-05-10  1h00m
            """;

        var result = PlaudCliClient.ParseFilesOutput(input);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("b6c774e4c9b2c55fa8159db2430726cc");
    }

    [Fact]
    public async Task ParseFileDetail_WithGeneratedFixture_ReturnsIsGeneratedTrue()
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_file_generated_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        var result = PlaudCliClient.ParseFileDetail(fixtureContent);

        result.TranscriptAvailable.Should().BeTrue();
        result.SummaryAvailable.Should().BeTrue();
        result.AudioAvailable.Should().BeTrue();
        result.IsGenerated.Should().BeTrue();
    }

    [Fact]
    public async Task ParseFileDetail_WithRawFixture_ReturnsIsGeneratedFalse()
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_file_raw_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        var result = PlaudCliClient.ParseFileDetail(fixtureContent);

        result.TranscriptAvailable.Should().BeFalse();
        result.SummaryAvailable.Should().BeFalse();
        result.AudioAvailable.Should().BeFalse();
        result.IsGenerated.Should().BeFalse();
    }

    [Fact]
    public void ParseFileDetail_IgnoresHeaderLines()
    {
        const string input = """
            - Fetching file...
            File Details:
              audio:        available
              transcript:   available
              summary:      unavailable
            """;

        var result = PlaudCliClient.ParseFileDetail(input);

        result.AudioAvailable.Should().BeTrue();
        result.TranscriptAvailable.Should().BeTrue();
        result.SummaryAvailable.Should().BeFalse();
        result.IsGenerated.Should().BeFalse();
    }
}
