using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
{
    private readonly IEntraAccessUserSource _source;
    private readonly ILogger<GetEntraAccessUsersHandler> _logger;

    public GetEntraAccessUsersHandler(IEntraAccessUserSource source, ILogger<GetEntraAccessUsersHandler> logger)
    {
        _source = source;
        _logger = logger;
    }

    public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
    {
        try
        {
            var users = await _source.GetBaseMembersAsync(ct);
            return new GetEntraAccessUsersResponse
            {
                Users = users.Select(u => new EntraUserDto
                {
                    EntraObjectId = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                }).OrderBy(u => u.DisplayName).ToList(),
            };
        }
        catch (EntraAccessSourceAuthException ex)
        {
            _logger.LogError(ex, "Failed to resolve Entra access users");
            return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ConfigurationError };
        }
        catch (EntraAccessSourceException ex)
        {
            _logger.LogError(ex, "Failed to resolve Entra access users");
            return new GetEntraAccessUsersResponse { Success = false, ErrorCode = ErrorCodes.ExternalServiceError };
        }
    }
}
