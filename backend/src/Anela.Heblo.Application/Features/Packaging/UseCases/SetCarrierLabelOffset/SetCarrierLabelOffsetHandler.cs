using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.SetCarrierLabelOffset;

public class SetCarrierLabelOffsetHandler : IRequestHandler<SetCarrierLabelOffsetRequest, SetCarrierLabelOffsetResponse>
{
    private readonly ICarrierLabelOffsetRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetCarrierLabelOffsetHandler(
        ICarrierLabelOffsetRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    }

    public async Task<SetCarrierLabelOffsetResponse> Handle(
        SetCarrierLabelOffsetRequest request,
        CancellationToken cancellationToken)
    {
        var currentUser = _currentUserService.GetCurrentUser();
        if (string.IsNullOrEmpty(currentUser.Id))
            return new SetCarrierLabelOffsetResponse(ErrorCodes.Unauthorized);

        var offset = new CarrierLabelOffset(request.Carrier, request.OffsetXMm, request.OffsetYMm, currentUser.Id);
        await _repository.UpsertAsync(offset, cancellationToken);

        return new SetCarrierLabelOffsetResponse();
    }
}
