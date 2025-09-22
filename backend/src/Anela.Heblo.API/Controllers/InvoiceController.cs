using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Invoice.Services;
using Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceDetail;
using Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceList;
using Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;
using Anela.Heblo.Application.Features.Invoice.UseCases.SearchInvoices;
using Anela.Heblo.Xcc.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoiceController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IBackgroundWorker _backgroundWorker;
    private readonly IInvoiceImportService _importService;

    public InvoiceController(
        IMediator mediator,
        IBackgroundWorker backgroundWorker,
        IInvoiceImportService importService)
    {
        _mediator = mediator;
        _backgroundWorker = backgroundWorker;
        _importService = importService;
    }

    /// <summary>
    /// Synchronously import invoices based on criteria
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportInvoicesResponse>> Import([FromBody] ImportInvoicesRequest request)
    {
        Logger.LogInformation("Starting synchronous invoice import");
        var result = await _mediator.Send(request);
        return HandleResponse(result);
    }

    /// <summary>
    /// Queue invoice import for background processing
    /// </summary>
    [HttpPost("import/enqueue")]
    public ActionResult<EnqueueImportResponse> EnqueueImport([FromBody] ImportInvoicesRequest request)
    {
        Logger.LogInformation("Queueing invoice import for background processing");
        
        try
        {
            var jobId = _backgroundWorker.Enqueue<IInvoiceImportService>(
                service => service.ImportBatchAsync(request.Criteria, CancellationToken.None)
            );
            
            var response = new EnqueueImportResponse
            {
                JobId = jobId,
                Message = "Import queued for processing",
                QueuedAt = DateTime.UtcNow
            };
            
            return HandleResponse(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to queue invoice import");
            
            var errorResponse = new EnqueueImportResponse(Application.Shared.ErrorCodes.InternalServerError);
            return HandleResponse(errorResponse);
        }
    }

    /// <summary>
    /// Get paginated list of imported invoices
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GetInvoiceListResponse>> GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        Logger.LogInformation("Getting invoice list for page {Page}, size {PageSize}", page, pageSize);
        
        var request = new GetInvoiceListRequest 
        { 
            Page = page, 
            PageSize = pageSize 
        };
        
        var result = await _mediator.Send(request);
        return HandleResponse(result);
    }

    /// <summary>
    /// Search invoices by term (invoice number, customer name, etc.)
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<SearchInvoicesResponse>> SearchInvoices([FromQuery] string searchTerm)
    {
        Logger.LogInformation("Searching invoices with term: {SearchTerm}", searchTerm);
        
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            var badRequestResponse = new SearchInvoicesResponse(Application.Shared.ErrorCodes.ValidationError, 
                new Dictionary<string, string> { { "searchTerm", "Search term is required" } })
            {
                SearchTerm = searchTerm ?? string.Empty
            };
            return HandleResponse(badRequestResponse);
        }
        
        var request = new SearchInvoicesRequest 
        { 
            SearchTerm = searchTerm.Trim() 
        };
        
        var result = await _mediator.Send(request);
        return HandleResponse(result);
    }

    /// <summary>
    /// Get invoice detail with all import attempts
    /// </summary>
    [HttpGet("{externalId}")]
    public async Task<ActionResult<GetInvoiceDetailResponse>> GetInvoiceDetail(string externalId)
    {
        Logger.LogInformation("Getting invoice detail for external ID: {ExternalId}", externalId);
        
        var request = new GetInvoiceDetailRequest 
        { 
            ExternalId = externalId 
        };
        
        var result = await _mediator.Send(request);
        return HandleResponse(result);
    }
}