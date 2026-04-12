using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.ResetGridLayout;

public class ResetGridLayoutHandler : IRequestHandler<ResetGridLayoutRequest, ResetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ResetGridLayoutHandler> _logger;

    public ResetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<ResetGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ResetGridLayoutResponse> Handle(ResetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        try
        {
            await _repository.DeleteAsync(userId, request.GridKey, cancellationToken);
            return new ResetGridLayoutResponse();
        }
        catch (Exception ex) when (ex is PostgresException or NpgsqlException)
        {
            var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
            _logger.LogError(ex,
                "Database error resetting GridLayout for user={UserId} gridKey={GridKey} SqlState={SqlState}",
                userId, request.GridKey, pgEx?.SqlState);
            return new ResetGridLayoutResponse(ErrorCodes.DatabaseError);
        }
    }
}
