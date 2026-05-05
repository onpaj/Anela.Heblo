using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class PdfTextExtractorTests
{
    private readonly PdfTextExtractor _extractor = new(NullLogger<PdfTextExtractor>.Instance);

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("APPLICATION/PDF")]
    public void CanHandle_ReturnsTrue_ForPdfContentType(string contentType)
    {
        Assert.True(_extractor.CanHandle(contentType));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("image/png")]
    public void CanHandle_ReturnsFalse_ForNonPdfContentTypes(string contentType)
    {
        Assert.False(_extractor.CanHandle(contentType));
    }

    [Fact]
    public void CleanPageText_ReplacesReplacementCharWithSpace()
    {
        // Arrange
        const string input = "hello�world";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void CleanPageText_CollapsesMultipleSpacesToOne()
    {
        // Arrange
        const string input = "hello  world";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void CleanPageText_PreservesNewlines()
    {
        // Arrange
        const string input = "line1\nline2";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("line1\nline2", result);
    }

    [Fact]
    public void CleanPageText_TrimsSurroundingWhitespace()
    {
        // Arrange
        const string input = "  trimmed  ";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("trimmed", result);
    }

    [Fact]
    public void CleanPageText_ReturnsEmptyString_WhenInputIsEmpty()
    {
        // Arrange
        const string input = "";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void CleanPageText_ReturnsEmptyString_WhenInputIsWhitespaceOnly()
    {
        // Arrange
        const string input = "   ";

        // Act
        var result = PdfTextExtractor.CleanPageText(input);

        // Assert
        Assert.Equal("", result);
    }
}
