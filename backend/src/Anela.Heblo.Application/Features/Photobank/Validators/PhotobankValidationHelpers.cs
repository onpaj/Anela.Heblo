using System;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.Photobank.Validators;

internal static class PhotobankValidationHelpers
{
    public static bool BeValidRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { _ = new Regex(pattern); return true; }
        catch (ArgumentException) { return false; }
    }
}
