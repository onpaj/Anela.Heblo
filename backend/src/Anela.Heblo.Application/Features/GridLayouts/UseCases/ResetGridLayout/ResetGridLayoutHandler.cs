using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutHandler : IRequestHandler<ResetGridLayoutRequest, ResetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public ResetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<ResetGridLayoutResponse> Handle(ResetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        await _repository.DeleteAsync(userId, request.GridKey, cancellationToken);

        return new ResetGridLayoutResponse();
    }
}
