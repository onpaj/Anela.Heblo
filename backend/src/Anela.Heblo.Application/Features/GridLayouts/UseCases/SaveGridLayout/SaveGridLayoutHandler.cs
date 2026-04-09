using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutHandler : IRequestHandler<SaveGridLayoutRequest, SaveGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SaveGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SaveGridLayoutResponse> Handle(SaveGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email ?? "anonymous";

        var payload = new GridLayoutDto
        {
            GridKey = request.GridKey,
            Columns = request.Columns
        };

        var json = JsonSerializer.Serialize(payload);
        await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);

        return new SaveGridLayoutResponse();
    }
}
