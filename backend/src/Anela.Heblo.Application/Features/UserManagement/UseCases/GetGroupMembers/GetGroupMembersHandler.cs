using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;

public class GetGroupMembersHandler : IRequestHandler<GetGroupMembersRequest, GetGroupMembersResponse>
{
    private readonly IGraphService _graphService;
    private readonly ILogger<GetGroupMembersHandler> _logger;

    public GetGroupMembersHandler(IGraphService graphService, ILogger<GetGroupMembersHandler> logger)
    {
        _graphService = graphService;
        _logger = logger;
    }

    public async Task<GetGroupMembersResponse> Handle(GetGroupMembersRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling GetGroupMembers request for group {GroupId}", request.GroupId);

        try
        {
            var members = await _graphService.GetGroupMembersAsync(request.GroupId, cancellationToken);

            return new GetGroupMembersResponse
            {
                Success = true,
                Members = members
            };
        }
        catch (GraphServiceAuthException ex)
        {
            _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

            return new GetGroupMembersResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ConfigurationError,
                Members = new List<UserDto>()
            };
        }
        catch (GraphServiceException ex)
        {
            _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

            return new GetGroupMembersResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ExternalServiceError,
                Members = new List<UserDto>()
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

            return new GetGroupMembersResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.Forbidden,
                Members = new List<UserDto>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle GetGroupMembers for {GroupId}", request.GroupId);

            return new GetGroupMembersResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InternalServerError,
                Members = new List<UserDto>()
            };
        }
    }
}