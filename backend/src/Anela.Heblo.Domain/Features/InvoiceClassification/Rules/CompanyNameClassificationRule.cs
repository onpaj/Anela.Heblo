namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class CompanyNameClassificationRule : IClassificationRule
{
    public string Identifier => "COMPANY_NAME";
    public string DisplayName => "Název firmy";
    public string Description => "Regex nebo text v názvu firmy";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.CompanyName) || string.IsNullOrWhiteSpace(pattern))
            return false;

        return ClassificationRegex.IsMatchOrContains(invoice.CompanyName, pattern);
    }
}