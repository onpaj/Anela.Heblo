namespace Anela.Heblo.Domain.Features.Invoices;

public class IssuedInvoiceClientException : Exception
{
    public string? RawAdapterResponse { get; }

    public IssuedInvoiceClientException(string message, string? rawAdapterResponse = null)
        : base(message)
    {
        RawAdapterResponse = rawAdapterResponse;
    }
}
