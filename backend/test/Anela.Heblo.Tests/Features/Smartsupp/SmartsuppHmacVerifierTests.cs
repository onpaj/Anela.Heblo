using System.Security.Cryptography;
using System.Text;
using Anela.Heblo.API.Webhooks.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppHmacVerifierTests
{
    private const string Secret = "shared-secret-for-tests";

    private static string ComputeSignature(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Verify_ReturnsTrue_WhenSignatureMatches()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\"}");
        var signature = ComputeSignature(body, Secret);

        var result = SmartsuppHmacVerifier.Verify(body, signature, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_IsCaseInsensitive_AndTrimsHeaderValue()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var signature = ComputeSignature(body, Secret);
        var asUpperWithSpaces = "  " + signature.ToUpperInvariant() + "  ";

        var result = SmartsuppHmacVerifier.Verify(body, asUpperWithSpaces, Secret);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSignatureMismatch()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "0000000000000000000000000000000000000000000000000000000000000000", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsNull()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, null, Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsEmpty()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenHeaderIsWhitespace()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        var result = SmartsuppHmacVerifier.Verify(body, "   ", Secret);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSecretIsEmpty()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var sigForEmptySecret = ComputeSignature(body, "");

        var result = SmartsuppHmacVerifier.Verify(body, sigForEmptySecret, "");

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenBodyTampered()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\"}");
        var signature = ComputeSignature(originalBody, Secret);

        var tamperedBody = Encoding.UTF8.GetBytes("{\"event\":\"conversation.deleted\"}");
        var result = SmartsuppHmacVerifier.Verify(tamperedBody, signature, Secret);

        result.Should().BeFalse();
    }
}
