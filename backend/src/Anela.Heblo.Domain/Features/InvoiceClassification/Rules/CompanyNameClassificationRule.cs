using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class CompanyNameClassificationRule : IClassificationRule
{
    public string Identifier => "COMPANY_NAME";
    public string DisplayName => "Název firmy";
    public string Description => "Regex nebo text v názvu firmy";

    public bool Evaluate(ReceivedInvoiceDto invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.CompanyName) || string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            return Regex.IsMatch(invoice.CompanyName, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return invoice.CompanyName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}