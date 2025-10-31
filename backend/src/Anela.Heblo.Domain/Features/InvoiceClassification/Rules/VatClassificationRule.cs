namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class VatClassificationRule : IClassificationRule
{
    public string Identifier => "ICO";
    public string DisplayName => "IČO";
    public string Description => "Porovnání IČO firmy";

    public bool Evaluate(ReceivedInvoiceDto invoice, string pattern)
    {
        return string.Equals(invoice.CompanyIco?.Trim(), pattern?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}