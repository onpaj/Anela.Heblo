using Anela.Heblo.Application.Features.KnowledgeBase;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase;

public class ContentTypeResolverTests
{
    [Theory]
    [InlineData("application/octet-stream", "x.pdf", "application/pdf")]
    [InlineData("application/octet-stream", "x.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/octet-stream", "x.doc", "application/msword")]
    [InlineData("application/octet-stream", "x.txt", "text/plain")]
    [InlineData("application/octet-stream", "x.md", "text/markdown")]
    public void Resolve_OctetStream_KnownExtension_ReturnsMappedMime(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "x.pdf", "application/pdf")]
    [InlineData("", "x.txt", "text/plain")]
    public void Resolve_EmptyContentType_KnownExtension_ReturnsMappedMime(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Fact]
    public void Resolve_NullContentType_KnownExtension_ReturnsMappedMime()
    {
        var result = ContentTypeResolver.Resolve(null!, "x.pdf");

        result.Should().Be("application/pdf");
    }

    [Fact]
    public void Resolve_OctetStream_UnknownExtension_ReturnsOriginalOctetStream()
    {
        var result = ContentTypeResolver.Resolve("application/octet-stream", "x.xyz");

        result.Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("image/png", "x.pdf", "image/png")]
    [InlineData("text/html", "x.docx", "text/html")]
    [InlineData("application/json", "x.xyz", "application/json")]
    public void Resolve_NonOctetStream_PassesThrough_RegardlessOfExtension(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("APPLICATION/OCTET-STREAM", "x.pdf", "application/pdf")]
    [InlineData("Application/Octet-Stream", "x.PDF", "application/pdf")]
    [InlineData("application/octet-stream", "x.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Resolve_CaseInsensitive_OctetStreamAndExtension_StillResolves(string contentType, string filename, string expected)
    {
        var result = ContentTypeResolver.Resolve(contentType, filename);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("application/octet-stream", "x.pdf")]
    [InlineData("application/octet-stream", "x.docx")]
    [InlineData("application/octet-stream", "x.doc")]
    [InlineData("application/octet-stream", "x.txt")]
    [InlineData("application/octet-stream", "x.md")]
    [InlineData("application/octet-stream", "x.xyz")]
    [InlineData("image/png", "x.pdf")]
    [InlineData("", "x.pdf")]
    public void Resolve_IsIdempotent(string contentType, string filename)
    {
        var first = ContentTypeResolver.Resolve(contentType, filename);
        var second = ContentTypeResolver.Resolve(first, filename);

        second.Should().Be(first);
    }
}
