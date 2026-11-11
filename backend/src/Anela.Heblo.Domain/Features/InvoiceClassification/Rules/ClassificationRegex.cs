using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

internal static class ClassificationRegex
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    internal static bool IsMatchOrContains(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, Timeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
