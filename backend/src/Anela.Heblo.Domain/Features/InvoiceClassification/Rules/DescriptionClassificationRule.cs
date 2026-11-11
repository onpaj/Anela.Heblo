namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class DescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "DESCRIPTION";
    public string DisplayName => "Popis faktury";
    public string Description => "Regex nebo text v popisu faktury";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.Description) || string.IsNullOrWhiteSpace(pattern))
            return false;

        return ClassificationRegex.IsMatchOrContains(invoice.Description, pattern);
    }
}