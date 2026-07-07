using AutoMapper;
using MediatR;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationHistory;

public class GetClassificationHistoryHandler : IRequestHandler<GetClassificationHistoryRequest, GetClassificationHistoryResponse>
{
    private readonly IClassificationHistoryRepository _historyRepository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetClassificationHistoryHandler> _logger;

    public GetClassificationHistoryHandler(
        IClassificationHistoryRepository historyRepository,
        IMapper mapper,
        ILogger<GetClassificationHistoryHandler> logger)
    {
        _historyRepository = historyRepository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<GetClassificationHistoryResponse> Handle(GetClassificationHistoryRequest request, CancellationToken cancellationToken)
    {
        var (historyItems, totalCount) = await _historyRepository.GetPagedHistoryAsync(
            request.Page,
            request.PageSize,
            request.FromDate,
            request.ToDate,
            request.InvoiceNumber,
            request.CompanyName);

        var historyDtos = _mapper.Map<List<ClassificationHistoryDto>>(historyItems);

        return new GetClassificationHistoryResponse
        {
            Items = historyDtos,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}