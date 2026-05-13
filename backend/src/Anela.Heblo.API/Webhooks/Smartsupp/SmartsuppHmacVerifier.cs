using System.Security.Cryptography;
using System.Text;

namespace Anela.Heblo.API.Webhooks.Smartsupp;

public static class SmartsuppHmacVerifier
{
    public static bool Verify(byte[] rawBody, string? headerValue, string secret)
    {
        if (string.IsNullOrEmpty(secret))
            return false;

        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        var normalizedHeader = headerValue.Trim().ToLowerInvariant();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(rawBody);
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        var headerBytes = Encoding.ASCII.GetBytes(normalizedHeader);
        var computedBytes = Encoding.ASCII.GetBytes(computedHex);

        if (headerBytes.Length != computedBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(headerBytes, computedBytes);
    }
}
