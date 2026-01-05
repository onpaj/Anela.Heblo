using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
        var query = _repository.GetAll();

        // Filter by state if provided
        if (request.State.HasValue)
        {
            query = query.Where(x => x.State == request.State.Value);
        }

        // Order by creation date descending (newest first)
        query = query.OrderByDescending(x => x.CreatedAt);

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var pageSize = request.PageSize ?? 50;
        var page = request.Page ?? 1;
        var skip = (page - 1) * pageSize;

        var operations = await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GetStockUpOperationsResponse
        {
            Operations = _mapper.Map<List<StockUpOperationDto>>(operations),
            TotalCount = totalCount,
            Success = true
        };
    }
}
