using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;

public class CreateEansHandler : IRequestHandler<CreateEansRequest, CreateEansResponse>
{
    private readonly ILogger<CreateEansHandler> _logger;
    private readonly IEanRepository _eanRepository;
    private readonly ILotRepository _lotRepository;
    private readonly IEanCodeGenerator _eanCodeGenerator;
    private readonly ICurrentUserService _currentUserService;

    public CreateEansHandler(
        ILogger<CreateEansHandler> logger,
        IEanRepository eanRepository,
        ILotRepository lotRepository,
        IEanCodeGenerator eanCodeGenerator,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _eanRepository = eanRepository;
        _lotRepository = lotRepository;
        _eanCodeGenerator = eanCodeGenerator;
        _currentUserService = currentUserService;
    }

    public async Task<CreateEansResponse> Handle(CreateEansRequest request, CancellationToken cancellationToken)
    {
        var lot = await _lotRepository.GetByIdAsync(request.LotId, cancellationToken);
        if (lot == null)
        {
            _logger.LogWarning("Lot {LotId} not found for EAN creation", request.LotId);
            return new CreateEansResponse(ErrorCodes.LotNotFound,
                new Dictionary<string, string> { { "LotId", request.LotId.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        var createdBy = currentUser.Name ?? "System";

        var codes = await _eanCodeGenerator.GenerateAsync(request.Items.Count, cancellationToken);
        var eans = request.Items
            .Select((item, i) => new Ean(codes[i], request.LotId, item.Amount, item.Unit, createdBy))
            .ToList();

        await _eanRepository.AddRangeAsync(eans, cancellationToken);
        await _eanRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created {Count} EANs for Lot {LotId}", eans.Count, request.LotId);

        return new CreateEansResponse
        {
            Eans = eans.Select(MapToDto).ToList()
        };
    }

    internal static EanDto MapToDto(Ean ean) => new()
    {
        Id = ean.Id,
        Code = ean.Code,
        LotId = ean.LotId,
        Amount = ean.Amount,
        Unit = ean.Unit,
        CreatedAt = ean.CreatedAt,
        CreatedBy = ean.CreatedBy
    };
}
