using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;

public class ListEansHandler : IRequestHandler<ListEansRequest, ListEansResponse>
{
    private readonly ILogger<ListEansHandler> _logger;
    private readonly IEanRepository _eanRepository;

    public ListEansHandler(ILogger<ListEansHandler> logger, IEanRepository eanRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
    }

    public async Task<ListEansResponse> Handle(ListEansRequest request, CancellationToken cancellationToken)
    {
        var result = await _eanRepository.GetPaginatedAsync(
            request.LotId,
            request.MaterialCode,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListEansResponse
        {
            Eans = result.Items.Select(CreateEansHandler.MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
