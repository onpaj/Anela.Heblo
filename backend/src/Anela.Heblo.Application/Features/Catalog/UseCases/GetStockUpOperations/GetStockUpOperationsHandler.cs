using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperations;

public class GetStockUpOperationsHandler : IRequestHandler<GetStockUpOperationsRequest, GetStockUpOperationsResponse>
{
    private readonly IStockUpOperationRepository _repository;
    private readonly IMapper _mapper;

    public GetStockUpOperationsHandler(IStockUpOperationRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<GetStockUpOperationsResponse> Handle(GetStockUpOperationsRequest request, CancellationToken cancellationToken)
    {
        var filter = new StockUpOperationFilter
        {
            State = request.State,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            ProductCode = request.ProductCode,
            DocumentNumber = request.DocumentNumber,
            CreatedFrom = request.CreatedFrom,
            CreatedTo = request.CreatedTo,
            SortBy = request.SortBy,
            SortDescending = request.SortDescending,
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 50,
        };

        var (operations, totalCount) = await _repository.QueryAsync(filter, cancellationToken);

        return new GetStockUpOperationsResponse
        {
            Operations = _mapper.Map<List<StockUpOperationDto>>(operations),
            TotalCount = totalCount,
            Success = true
        };
    }
}
