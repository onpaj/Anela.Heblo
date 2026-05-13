using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudCliClientParserTests
{
    [Fact]
    public async Task ParseFilesOutput_WithFixture_ParsesMultipleRecordings()
    {
        // Arrange
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_recent_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        // Act
        var result = PlaudCliClient.ParseFilesOutput(fixtureContent);

        // Assert
        result.Should().HaveCount(3);

        // First record: rec_001
        var first = result[0];
        first.Id.Should().Be("rec_001");
        first.Name.Should().Be("Weekly Team Standup");
        first.HasTranscript.Should().BeTrue();
        first.HasSummary.Should().BeTrue();

        // Second record: rec_002
        var second = result[1];
        second.Id.Should().Be("rec_002");
        second.Name.Should().Be("Product Review Meeting");
        second.HasTranscript.Should().BeTrue();
        second.HasSummary.Should().BeFalse();

        // Third record: rec_003
        var third = result[2];
        third.Id.Should().Be("rec_003");
        third.Name.Should().Be("Quick Sync");
        third.HasTranscript.Should().BeFalse();
        third.HasSummary.Should().BeFalse();
    }

    [Fact]
    public async Task ParseFilesOutput_WithFixture_IdentifiesReadyRecordings()
    {
        // Arrange
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_recent_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        // Act
        var result = PlaudCliClient.ParseFilesOutput(fixtureContent);
        var readyRecordings = result.Where(r => r.HasTranscript && r.HasSummary).ToList();

        // Assert
        readyRecordings.Should().HaveCount(1);
        readyRecordings[0].Id.Should().Be("rec_001");
    }

    [Fact]
    public void ParseFilesOutput_WithEmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var emptyInput = string.Empty;

        // Act
        var result = PlaudCliClient.ParseFilesOutput(emptyInput);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilesOutput_WithHeaderOnly_ReturnsEmptyList()
    {
        // Arrange
        var headerOnlyInput = "ID          NAME                                DATE        TIME    TRANSCRIPT  SUMMARY";

        // Act
        var result = PlaudCliClient.ParseFilesOutput(headerOnlyInput);

        // Assert
        result.Should().BeEmpty();
    }
}
