using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;

public class ClassifyInvoicesResponse : BaseResponse
{
    public int TotalInvoicesProcessed { get; set; }

    public int SuccessfulClassifications { get; set; }

    public int ManualReviewRequired { get; set; }

    public int Errors { get; set; }

    public List<string> ErrorMessages { get; set; } = new();
}