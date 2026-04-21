using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.DataQuality;

public class InvoiceDqtResult : Entity<Guid>
{
    public Guid DqtRunId { get; private set; }
    public string InvoiceCode { get; private set; } = string.Empty;
    public InvoiceMismatchType MismatchType { get; private set; }
    public string? ShoptetValue { get; private set; }
    public string? FlexiValue { get; private set; }
    public string? Details { get; private set; }

    private InvoiceDqtResult() { } // EF Core

    public static InvoiceDqtResult Create(
        Guid dqtRunId,
        string invoiceCode,
        InvoiceMismatchType mismatchType,
        string? shoptetValue,
        string? flexiValue,
        string? details)
    {
        return new InvoiceDqtResult
        {
            Id = Guid.NewGuid(),
            DqtRunId = dqtRunId,
            InvoiceCode = invoiceCode,
            MismatchType = mismatchType,
            ShoptetValue = shoptetValue,
            FlexiValue = flexiValue,
            Details = details
        };
    }
}
