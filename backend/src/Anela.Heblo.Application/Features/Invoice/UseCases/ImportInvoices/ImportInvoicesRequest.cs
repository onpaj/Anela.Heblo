using MediatR;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;

public class ImportInvoicesRequest : IRequest<ImportInvoicesResponse>
{
    public BatchCriteria? Criteria { get; set; }
}

public class BatchCriteria
{
    /// <summary>
    /// Start date for date range import
    /// </summary>
    public DateTime? FromDate { get; set; }
    
    /// <summary>
    /// End date for date range import
    /// </summary>
    public DateTime? ToDate { get; set; }
    
    /// <summary>
    /// Specific invoice numbers to import
    /// </summary>
    public List<string>? InvoiceNumbers { get; set; }
    
    /// <summary>
    /// Maximum number of invoices to import in one batch
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;
    
    /// <summary>
    /// Currency filter for invoices
    /// </summary>
    public string? Currency { get; set; }
}