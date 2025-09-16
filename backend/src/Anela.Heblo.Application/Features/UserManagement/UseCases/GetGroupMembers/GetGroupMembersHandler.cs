using Anela.Heblo.Application.Features.UserManagement.Services;
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
        try
        {
            _logger.LogInformation("Handling GetGroupMembers request for group {GroupId}", request.GroupId);

            var members = await _graphService.GetGroupMembersAsync(request.GroupId, cancellationToken);

            return new GetGroupMembersResponse
            {
                Success = true,
                Members = members
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling GetGroupMembers request for group {GroupId}", request.GroupId);

            return new GetGroupMembersResponse
            {
                Success = false,
                ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.InternalServerError,
                Members = new List<Anela.Heblo.Application.Features.UserManagement.Contracts.UserDto>()
            };
        }
    }
}