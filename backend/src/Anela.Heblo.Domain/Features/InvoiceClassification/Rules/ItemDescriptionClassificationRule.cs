using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class ItemDescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "ITEM_DESCRIPTION";
    public string DisplayName => "Popis položky";
    public string Description => "Regex nebo text v popisu některé položky faktury";

    public bool Evaluate(ReceivedInvoiceDto invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        return invoice.Items.Any(item => EvaluateItemDescription(item.Name, pattern));
    }

    private bool EvaluateItemDescription(string itemDescription, string pattern)
    {
        if (string.IsNullOrWhiteSpace(itemDescription))
            return false;

        try
        {
            return Regex.IsMatch(itemDescription, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return itemDescription.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}