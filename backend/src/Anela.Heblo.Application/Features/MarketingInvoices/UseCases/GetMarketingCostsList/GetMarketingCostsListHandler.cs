using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;

public class GetMarketingCostsListHandler : IRequestHandler<GetMarketingCostsListRequest, GetMarketingCostsListResponse>
{
    private readonly IImportedMarketingTransactionRepository _repository;

    public GetMarketingCostsListHandler(IImportedMarketingTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMarketingCostsListResponse> Handle(GetMarketingCostsListRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Platform,
            request.DateFrom,
            request.DateTo,
            request.IsSynced,
            request.SortBy,
            request.SortDescending,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(x => new MarketingCostListItemDto
        {
            Id = x.Id,
            TransactionId = x.TransactionId,
            Platform = x.Platform,
            Amount = x.Amount,
            Currency = x.Currency,
            TransactionDate = x.TransactionDate,
            ImportedAt = x.ImportedAt,
            IsSynced = x.IsSynced,
        }).ToList();

        return new GetMarketingCostsListResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
