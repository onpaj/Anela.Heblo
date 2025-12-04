using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;

public class EnqueueImportInvoicesResponse : BaseResponse
{
    public string? JobId { get; set; }

    public EnqueueImportInvoicesResponse() : base()
    {
    }

    public EnqueueImportInvoicesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters)
    {
    }
}