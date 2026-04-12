using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;

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

        var payload = new GridLayoutDto
        {
            GridKey = request.GridKey,
            Columns = request.Columns
        };

        var json = JsonSerializer.Serialize(payload);

        try
        {
            await _repository.UpsertAsync(userId, request.GridKey, json, cancellationToken);
            return new SaveGridLayoutResponse();
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException)
        {
            var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
            _logger.LogError(ex,
                "Database error saving GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, pgEx?.SqlState);
            return new SaveGridLayoutResponse(ErrorCodes.DatabaseError);
        }
    }
}
