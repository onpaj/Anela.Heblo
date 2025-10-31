using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class DescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "DESCRIPTION";
    public string DisplayName => "Popis faktury";
    public string Description => "Regex nebo text v popisu faktury";

    public bool Evaluate(ReceivedInvoiceDto invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.Description) || string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            return Regex.IsMatch(invoice.Description, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return invoice.Description.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}