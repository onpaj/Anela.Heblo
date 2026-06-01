using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;

public class DeleteEanHandler : IRequestHandler<DeleteEanRequest, DeleteEanResponse>
{
    private readonly ILogger<DeleteEanHandler> _logger;
    private readonly IEanRepository _eanRepository;

    public DeleteEanHandler(ILogger<DeleteEanHandler> logger, IEanRepository eanRepository)
    {
        _logger = logger;
        _eanRepository = eanRepository;
    }

    public async Task<DeleteEanResponse> Handle(DeleteEanRequest request, CancellationToken cancellationToken)
    {
        var ean = await _eanRepository.GetByIdAsync(request.Id, cancellationToken);
        if (ean == null)
        {
            _logger.LogWarning("EAN {Id} not found for delete", request.Id);
            return new DeleteEanResponse(ErrorCodes.EanNotFound, new Dictionary<string, string> { { "Id", request.Id.ToString() } });
        }

        await _eanRepository.DeleteAsync(ean, cancellationToken);
        await _eanRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("EAN {Id} deleted", request.Id);
        return new DeleteEanResponse();
    }
}
