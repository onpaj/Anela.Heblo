using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;

public class GetMarketingCostDetailHandler : IRequestHandler<GetMarketingCostDetailRequest, GetMarketingCostDetailResponse>
{
    private readonly IImportedMarketingTransactionRepository _repository;

    public GetMarketingCostDetailHandler(IImportedMarketingTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMarketingCostDetailResponse> Handle(GetMarketingCostDetailRequest request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (entity == null)
        {
            return new GetMarketingCostDetailResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ResourceNotFound,
            };
        }

        return new GetMarketingCostDetailResponse
        {
            Item = new MarketingCostDetailDto
            {
                Id = entity.Id,
                TransactionId = entity.TransactionId,
                Platform = entity.Platform,
                Amount = entity.Amount,
                Currency = entity.Currency,
                TransactionDate = entity.TransactionDate,
                ImportedAt = entity.ImportedAt,
                IsSynced = entity.IsSynced,
                Description = entity.Description,
                ErrorMessage = entity.ErrorMessage,
                RawData = entity.RawData,
            }
        };
    }
}
