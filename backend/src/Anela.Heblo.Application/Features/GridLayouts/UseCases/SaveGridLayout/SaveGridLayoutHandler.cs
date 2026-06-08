using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.SaveGridLayout;

public class SaveGridLayoutHandler : IRequestHandler<SaveGridLayoutRequest, SaveGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SaveGridLayoutHandler> _logger;

    public SaveGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<SaveGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<SaveGridLayoutResponse> Handle(SaveGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        var stored = GridLayoutStoredMapper.ToStored(request.Columns);
        var json = JsonSerializer.Serialize(stored);

        try
        {
            await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);
            return new SaveGridLayoutResponse();
        }
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error saving GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, ex.SqlState);
            return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);
        }
    }
}
