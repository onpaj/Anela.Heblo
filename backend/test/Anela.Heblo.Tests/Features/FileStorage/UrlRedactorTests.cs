using Anela.Heblo.Domain.Features.FileStorage;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class UrlRedactorTests
{
    [Fact]
    public void Redact_StripsQuery()
    {
        var result = UrlRedactor.Redact("https://example.com/file.png?token=abc");
        Assert.DoesNotContain("token", result);
        Assert.DoesNotContain("abc", result);
        Assert.StartsWith("https://example.com/file.png", result);
    }

    [Fact]
    public void Redact_StripsFragment()
    {
        var result = UrlRedactor.Redact("https://example.com/file.png#secret");
        Assert.DoesNotContain("secret", result);
        Assert.StartsWith("https://example.com/file.png", result);
    }

    [Fact]
    public void Redact_StripsUserInfo()
    {
        var result = UrlRedactor.Redact("https://user:pass@example.com/file.png");
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.Contains("example.com/file.png", result);
    }

    [Theory]
    [InlineData("/relative/path?x=1")]
    [InlineData("not a url")]
    public void Redact_InvalidOrRelative_ReturnsRedacted(string input)
    {
        Assert.Equal("[redacted]", UrlRedactor.Redact(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Redact_NullOrEmpty_ReturnsRedacted(string? input)
    {
        Assert.Equal("[redacted]", UrlRedactor.Redact(input));
    }

    [Fact]
    public void Redact_PlainUrl_Unchanged()
    {
        Assert.Equal("https://example.com/file.png", UrlRedactor.Redact("https://example.com/file.png"));
    }
}
