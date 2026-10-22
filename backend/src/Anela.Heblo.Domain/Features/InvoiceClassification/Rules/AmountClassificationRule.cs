using System.Globalization;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class AmountClassificationRule : IClassificationRule
{
    public string Identifier => "AMOUNT";
    public string DisplayName => "Částka";
    public string Description => "Porovnání celkové částky faktury (>=, <=, >, <, =); desetinná tečka, např. >=1000.50";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            if (pattern.StartsWith(">="))
            {
                return TryParseAmount(pattern.Substring(2), out var minValue) && invoice.TotalAmount >= minValue;
            }
            if (pattern.StartsWith("<="))
            {
                return TryParseAmount(pattern.Substring(2), out var maxValue) && invoice.TotalAmount <= maxValue;
            }
            if (pattern.StartsWith(">"))
            {
                return TryParseAmount(pattern.Substring(1), out var minValue) && invoice.TotalAmount > minValue;
            }
            if (pattern.StartsWith("<"))
            {
                return TryParseAmount(pattern.Substring(1), out var maxValue) && invoice.TotalAmount < maxValue;
            }
            if (pattern.StartsWith("="))
            {
                return TryParseAmount(pattern.Substring(1), out var exactValue) && invoice.TotalAmount == exactValue;
            }

            return TryParseAmount(pattern, out var value) && invoice.TotalAmount == value;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseAmount(string text, out decimal value)
    {
        return decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out value);
    }
}
