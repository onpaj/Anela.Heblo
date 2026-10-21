namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class ItemDescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "ITEM_DESCRIPTION";
    public string DisplayName => "Popis položky";
    public string Description => "Regex nebo text v popisu některé položky faktury";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        return invoice.Items.Any(item => EvaluateItemDescription(item.Name, pattern));
    }

    private bool EvaluateItemDescription(string itemDescription, string pattern)
    {
        if (string.IsNullOrWhiteSpace(itemDescription))
            return false;

        return ClassificationRegex.IsMatchOrContains(itemDescription, pattern);
    }
}