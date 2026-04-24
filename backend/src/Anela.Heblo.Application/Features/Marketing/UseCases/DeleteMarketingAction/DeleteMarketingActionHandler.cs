using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.DeleteMarketingAction
{
    public class DeleteMarketingActionHandler : IRequestHandler<DeleteMarketingActionRequest, DeleteMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<DeleteMarketingActionHandler> _logger;

        public DeleteMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<DeleteMarketingActionHandler> logger)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<DeleteMarketingActionResponse> Handle(
            DeleteMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new DeleteMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess, new Dictionary<string, string>
                {
                    { "resource", "marketing_action" },
                });
            }

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new DeleteMarketingActionResponse(ErrorCodes.MarketingActionNotFound, new Dictionary<string, string>
                {
                    { "actionId", request.Id.ToString() },
                });
            }

            await _repository.DeleteSoftAsync(request.Id, currentUser.Id, currentUser.Name ?? "Unknown User", cancellationToken);

            _logger.LogInformation(
                "MarketingAction {ActionId} deleted by user {UserId}",
                request.Id,
                currentUser.Id);

            return new DeleteMarketingActionResponse { Id = request.Id };
        }
    }
}
