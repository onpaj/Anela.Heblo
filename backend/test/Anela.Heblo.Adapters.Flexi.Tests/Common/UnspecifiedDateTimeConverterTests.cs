using Anela.Heblo.Adapters.Flexi.Common;
using FluentAssertions;
using Newtonsoft.Json;

namespace Anela.Heblo.Adapters.Flexi.Tests.Common;

public sealed class UnspecifiedDateTimeConverterTests
{
    private static DateTime? Invoke(string json, bool nullable = false, DateParseHandling dateParseHandling = DateParseHandling.None)
    {
        using var reader = new JsonTextReader(new StringReader(json));
        reader.DateParseHandling = dateParseHandling;
        reader.Read();
        var converter = new UnspecifiedDateTimeConverter();
        var targetType = nullable ? typeof(DateTime?) : typeof(DateTime);
        return (DateTime?)converter.ReadJson(reader, targetType, null, new JsonSerializer());
    }

    [Fact]
    public void ReadJson_WithNullToken_AndNullableType_ReturnsNull()
    {
        // Act
        var result = Invoke("null", nullable: true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ReadJson_WithNullToken_AndNonNullableType_ThrowsJsonSerializationException()
    {
        // Act
        var act = () => Invoke("null", nullable: false);

        // Assert
        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void ReadJson_WithValidIsoDateString_ReturnsUtcDateTime()
    {
        // Arrange
        var expectedLocal = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = expectedLocal - TimeZoneInfo.Local.GetUtcOffset(expectedLocal);

        // Act
        var result = Invoke("\"2024-03-15\"");

        // Assert
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(expectedUtc);
    }

    [Fact]
    public void ReadJson_WithValidIsoDateTimeString_ReturnsUtcDateTime()
    {
        // Arrange
        var expectedLocal = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Unspecified);
        var expectedUtc = expectedLocal - TimeZoneInfo.Local.GetUtcOffset(expectedLocal);

        // Act
        var result = Invoke("\"2024-03-15T10:30:00\"");

        // Assert
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(expectedUtc);
    }

    [Fact]
    public void ReadJson_WithEmptyString_ThrowsJsonSerializationException()
    {
        // Act
        var act = () => Invoke("\"\"");

        // Assert
        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void ReadJson_WithUnparseableString_ThrowsJsonSerializationException()
    {
        // Act
        var act = () => Invoke("\"not-a-date\"");

        // Assert
        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void ReadJson_WithDateToken_ReturnsUtcDateTime()
    {
        // Arrange
        var expectedLocal = new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = expectedLocal - TimeZoneInfo.Local.GetUtcOffset(expectedLocal);

        // Act — DateParseHandling.DateTime causes the reader to emit a JsonToken.Date
        var result = Invoke("\"2024-06-01T08:00:00\"", dateParseHandling: DateParseHandling.DateTime);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(expectedUtc);
    }

    [Fact]
    public void ReadJson_WithIntegerToken_ThrowsJsonSerializationException()
    {
        // Act
        var act = () => Invoke("42");

        // Assert
        act.Should().Throw<JsonSerializationException>();
    }

    [Fact]
    public void ReadJson_WithBooleanToken_ThrowsJsonSerializationException()
    {
        // Act
        var act = () => Invoke("true");

        // Assert
        act.Should().Throw<JsonSerializationException>();
    }
}
