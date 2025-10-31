namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationRule
{
    string Identifier { get; }
    string DisplayName { get; }
    string Description { get; }
    bool Evaluate(ReceivedInvoiceDto invoice, string pattern);
}