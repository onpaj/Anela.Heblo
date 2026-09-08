using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;

public class NewToOpenedSideEffect : ITransportBoxTransitionSideEffect
{
    private readonly ITransportBoxRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public NewToOpenedSideEffect(
        ITransportBoxRepository repository,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public bool Supports(TransportBoxState from, TransportBoxState to) =>
        from == TransportBoxState.New && to == TransportBoxState.Opened;

    public async Task<ChangeTransportBoxStateResponse?> ExecuteAsync(
        TransportBox box, ChangeTransportBoxStateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.BoxCode))
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.RequiredFieldMissing,
                Params = new Dictionary<string, string> { { "field", "BoxCode" } }
            };
        }

        // Check if another active box with the same code already exists
        var normalizedCode = request.BoxCode.ToUpper();
        var isCodeActive = await _repository.IsBoxCodeActiveAsync(normalizedCode);
        if (isCodeActive)
        {
            return new ChangeTransportBoxStateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                Params = new Dictionary<string, string> { { "code", normalizedCode } }
            };
        }

        // Close all stocked boxes
        var (stocked, _) = await _repository.GetPagedListAsync(skip: 0, take: 0, code: request.BoxCode, state: TransportBoxState.Stocked);
        foreach (var s in stocked)
        {
            s.Close(_timeProvider.GetUtcNow().UtcDateTime, _currentUserService.GetCurrentUser().Name ?? "System");
            await _repository.UpdateAsync(s, cancellationToken);
        }

        return null;
    }
}
