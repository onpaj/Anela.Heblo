namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class AmountClassificationRule : IClassificationRule
{
    public string Identifier => "AMOUNT";
    public string DisplayName => "Částka";
    public string Description => "Porovnání celkové částky faktury (>=, <=, >, <, =)";

    public bool Evaluate(ReceivedInvoiceDto invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            if (pattern.StartsWith(">="))
            {
                return decimal.TryParse(pattern.Substring(2), out var minValue) && invoice.TotalAmount >= minValue;
            }
            if (pattern.StartsWith("<="))
            {
                return decimal.TryParse(pattern.Substring(2), out var maxValue) && invoice.TotalAmount <= maxValue;
            }
            if (pattern.StartsWith(">"))
            {
                return decimal.TryParse(pattern.Substring(1), out var minValue) && invoice.TotalAmount > minValue;
            }
            if (pattern.StartsWith("<"))
            {
                return decimal.TryParse(pattern.Substring(1), out var maxValue) && invoice.TotalAmount < maxValue;
            }
            if (pattern.StartsWith("="))
            {
                return decimal.TryParse(pattern.Substring(1), out var exactValue) && invoice.TotalAmount == exactValue;
            }

            return decimal.TryParse(pattern, out var value) && invoice.TotalAmount == value;
        }
        catch
        {
            return false;
        }
    }
}