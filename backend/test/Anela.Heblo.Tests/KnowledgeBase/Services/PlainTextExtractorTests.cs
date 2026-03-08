using System.Text;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new(NullLogger<PlainTextExtractor>.Instance);

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("text/csv")]
    [InlineData("application/markdown")]
    public void CanHandle_ReturnsTrue_ForTextContentTypes(string contentType)
    {
        Assert.True(_extractor.CanHandle(contentType));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("image/png")]
    public void CanHandle_ReturnsFalse_ForNonTextContentTypes(string contentType)
    {
        Assert.False(_extractor.CanHandle(contentType));
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsUtf8DecodedString()
    {
        const string expected = "Příliš žluťoučký kůň";
        var bytes = Encoding.UTF8.GetBytes(expected);

        var result = await _extractor.ExtractTextAsync(bytes);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyBytes_ReturnsEmptyString()
    {
        var result = await _extractor.ExtractTextAsync([]);
        Assert.Equal(string.Empty, result);
    }
}
