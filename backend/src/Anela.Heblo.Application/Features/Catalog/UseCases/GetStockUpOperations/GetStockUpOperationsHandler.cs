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

        // Filter by state if provided (supports "Active" special value)
        if (!string.IsNullOrWhiteSpace(request.State))
        {
            if (request.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                // Active = Pending OR Submitted OR Failed (not Completed)
                query = query.Where(x =>
                    x.State == StockUpOperationState.Pending ||
                    x.State == StockUpOperationState.Submitted ||
                    x.State == StockUpOperationState.Failed);
            }
            else if (Enum.TryParse<StockUpOperationState>(request.State, true, out var parsedState))
            {
                query = query.Where(x => x.State == parsedState);
            }
        }

        // Filter by SourceType if provided
        if (request.SourceType.HasValue)
        {
            query = query.Where(x => x.SourceType == request.SourceType.Value);
        }

        // Filter by SourceId if provided
        if (request.SourceId.HasValue)
        {
            query = query.Where(x => x.SourceId == request.SourceId.Value);
        }

        // Filter by ProductCode (exact match) if provided
        if (!string.IsNullOrWhiteSpace(request.ProductCode))
        {
            query = query.Where(x => x.ProductCode == request.ProductCode);
        }

        // Filter by DocumentNumber (partial match, case-insensitive) if provided
        if (!string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            query = query.Where(x => x.DocumentNumber.ToLower().Contains(request.DocumentNumber.ToLower()));
        }

        // Filter by date range if provided
        if (request.CreatedFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= request.CreatedFrom.Value);
        }

        if (request.CreatedTo.HasValue)
        {
            // Include the entire day by adding 1 day and using < instead of <=
            var endDate = request.CreatedTo.Value.Date.AddDays(1);
            query = query.Where(x => x.CreatedAt < endDate);
        }

        // Apply sorting
        var sortBy = request.SortBy?.ToLower() ?? "createdAt";
        query = sortBy switch
        {
            "id" => request.SortDescending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
            "documentnumber" => request.SortDescending ? query.OrderByDescending(x => x.DocumentNumber) : query.OrderBy(x => x.DocumentNumber),
            "productcode" => request.SortDescending ? query.OrderByDescending(x => x.ProductCode) : query.OrderBy(x => x.ProductCode),
            "state" => request.SortDescending ? query.OrderByDescending(x => x.State) : query.OrderBy(x => x.State),
            "createdat" => request.SortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt) // Default sort
        };

        // Get total count after filtering but before pagination
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
