using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutHandler : IRequestHandler<GetGridLayoutRequest, GetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public GetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<GetGridLayoutResponse> Handle(GetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        var entity = await _repository.GetAsync(userId, request.GridKey, cancellationToken);

        if (entity is null)
        {
            return new GetGridLayoutResponse { Layout = null };
        }

        var dto = JsonSerializer.Deserialize<GridLayoutDto>(entity.LayoutJson) ?? new GridLayoutDto();
        dto.GridKey = entity.GridKey;
        dto.LastModified = entity.LastModified;

        return new GetGridLayoutResponse { Layout = dto };
    }
}
