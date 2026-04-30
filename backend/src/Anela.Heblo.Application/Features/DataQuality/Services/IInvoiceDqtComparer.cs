using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public interface IInvoiceDqtComparer
{
    Task<InvoiceDqtComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public class InvoiceDqtComparisonResult
{
    public List<InvoiceDqtMismatch> Mismatches { get; init; } = new();
    public int TotalChecked { get; init; }
}

public class InvoiceDqtMismatch
{
    public string InvoiceCode { get; init; } = string.Empty;
    public InvoiceMismatchType MismatchType { get; init; }
    public string? ShoptetValue { get; init; }
    public string? FlexiValue { get; init; }
    public string? Details { get; init; }
}
