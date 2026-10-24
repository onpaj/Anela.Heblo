# Design: Regex timeout in InvoiceClassification rules

## Component Design
`internal static class ClassificationRegex` (namespace `Anela.Heblo.Domain.Features.InvoiceClassification.Rules`):

```csharp
internal static class ClassificationRegex
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    internal static bool IsMatchOrContains(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, Timeout); }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException) { return input.Contains(pattern, StringComparison.OrdinalIgnoreCase); }
    }
}
```

`CompanyNameClassificationRule`, `DescriptionClassificationRule`: keep null/whitespace guards, then `return ClassificationRegex.IsMatchOrContains(field, pattern);`.
`ItemDescriptionClassificationRule.EvaluateItemDescription`: same delegation, per item.

If tests are in another assembly, `InternalsVisibleTo` is not needed as tests exercise the public rules.

## Data Schemas
None.
