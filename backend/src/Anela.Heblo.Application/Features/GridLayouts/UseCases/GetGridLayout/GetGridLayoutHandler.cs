using System.Text.Json;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.Infrastructure;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;

public class GetGridLayoutHandler : IRequestHandler<GetGridLayoutRequest, GetGridLayoutResponse>
{
    private readonly IGridLayoutRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetGridLayoutHandler> _logger;

    public GetGridLayoutHandler(IGridLayoutRepository repository, ICurrentUserService currentUserService, ILogger<GetGridLayoutHandler> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GetGridLayoutResponse> Handle(GetGridLayoutRequest request, CancellationToken cancellationToken)
    {
        var user = _currentUserService.GetCurrentUser();
        var userId = user.Id ?? user.Email
            ?? throw new InvalidOperationException("Authenticated user must have either Id or Email claim.");

        try
        {
            var entity = await _repository.GetAsync(userId, request.GridKey, cancellationToken);

            if (entity is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            StoredGridLayout? stored;
            try
            {
                stored = JsonSerializer.Deserialize<StoredGridLayout>(entity.LayoutJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout",
                    userId, request.GridKey);
                return new GetGridLayoutResponse { Layout = null };
            }

            if (stored is null)
            {
                return new GetGridLayoutResponse { Layout = null };
            }

            var dto = new GridLayoutDto
            {
                GridKey = entity.GridKey,
                Columns = GridLayoutStoredMapper.ToDtoColumns(stored),
                LastModified = entity.LastModified
            };

            return new GetGridLayoutResponse { Layout = dto };
        }
        catch (GridLayoutPersistenceException ex)
        {
            _logger.LogError(ex,
                "Database error reading GridLayout for user={UserId} gridKey={GridKey}",
                userId, request.GridKey);
            return new GetGridLayoutResponse { Layout = null };
        }
    }
}
