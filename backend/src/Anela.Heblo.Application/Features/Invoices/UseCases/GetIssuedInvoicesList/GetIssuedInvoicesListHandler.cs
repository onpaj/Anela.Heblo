using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;

/// <summary>
/// Handler for getting paginated list of issued invoices
/// </summary>
public class GetIssuedInvoicesListHandler : IRequestHandler<GetIssuedInvoicesListRequest, GetIssuedInvoicesListResponse>
{
    private readonly IIssuedInvoiceRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetIssuedInvoicesListHandler> _logger;

    public GetIssuedInvoicesListHandler(
        IIssuedInvoiceRepository repository,
        IMapper mapper,
        ILogger<GetIssuedInvoicesListHandler> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetIssuedInvoicesListResponse> Handle(GetIssuedInvoicesListRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting issued invoices list, Page: {PageNumber}, Size: {PageSize}", 
                request.PageNumber, request.PageSize);

            // Create filters object
            var filters = new IssuedInvoiceFilters
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                SortBy = request.SortBy,
                SortDescending = request.SortDescending,
                InvoiceId = request.InvoiceId,
                CustomerName = request.CustomerName,
                InvoiceDateFrom = request.InvoiceDateFrom,
                InvoiceDateTo = request.InvoiceDateTo,
                IsSynced = request.IsSynced,
                ShowOnlyUnsynced = request.ShowOnlyUnsynced,
                ShowOnlyWithErrors = request.ShowOnlyWithErrors
            };

            // Get paginated results directly from database
            var paginatedResult = await _repository.GetPaginatedAsync(filters, cancellationToken);

            // Map to DTOs
            var dtos = _mapper.Map<List<IssuedInvoiceDto>>(paginatedResult.Items);

            _logger.LogInformation("Retrieved {Count} issued invoices out of {Total}", 
                dtos.Count, paginatedResult.TotalCount);

            return new GetIssuedInvoicesListResponse
            {
                Items = dtos,
                TotalCount = paginatedResult.TotalCount,
                PageNumber = paginatedResult.PageNumber,
                PageSize = paginatedResult.PageSize,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while getting issued invoices list");
            return new GetIssuedInvoicesListResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Exception,
                Params = new Dictionary<string, string> { { "ErrorMessage", "Chyba při načítání seznamu vydaných faktur" } }
            };
        }
    }

}