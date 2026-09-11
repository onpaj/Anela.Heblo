using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;

public class ResolvePriceSyncConflictHandler
    : IRequestHandler<ResolvePriceSyncConflictRequest, ResolvePriceSyncConflictResponse>
{
    private readonly IProductPriceRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public ResolvePriceSyncConflictHandler(IProductPriceRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<ResolvePriceSyncConflictResponse> Handle(
        ResolvePriceSyncConflictRequest request, CancellationToken cancellationToken)
    {
        var state = await _repository.GetSyncStateAsync(request.ProductCode, request.Target, cancellationToken);
        if (state is null || state.Status != PriceSyncStatus.Conflict)
        {
            return new ResolvePriceSyncConflictResponse(
                ErrorCodes.ProductPriceConflictNotFound,
                new Dictionary<string, string>
                {
                    ["ProductCode"] = request.ProductCode,
                    ["Target"] = request.Target.ToString(),
                });
        }

        var remoteValue = state.RemoteValueAtConflict;

        if (request.Resolution == PriceConflictResolution.AcceptRemotePrice)
        {
            var price = await _repository.GetAsync(request.ProductCode, cancellationToken);
            if (price is null)
            {
                return new ResolvePriceSyncConflictResponse(
                    ErrorCodes.ProductPriceNotFound,
                    new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
            }

            price.PriceWithVat = remoteValue!.Value;
            price.ModifiedAt = DateTime.UtcNow;
            price.ModifiedBy = _currentUserService.GetCurrentUser().Email;
            await _repository.UpsertAsync(price, cancellationToken);

            state.Status = PriceSyncStatus.InSync;
        }
        else
        {
            // Rebasing LastPushed onto the remote value turns the next run's compare into
            // "Heblo changed, remote didn't", which pushes and overwrites the downstream edit.
            state.Status = PriceSyncStatus.Pending;
        }

        state.LastPushedPriceWithVat = remoteValue;
        state.RemoteValueAtConflict = null;
        state.ConflictDetectedAt = null;
        state.LastError = null;
        await _repository.UpsertSyncStateAsync(state, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ResolvePriceSyncConflictResponse();
    }
}
