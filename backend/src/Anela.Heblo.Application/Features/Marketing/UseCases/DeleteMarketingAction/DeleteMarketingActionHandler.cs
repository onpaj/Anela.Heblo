using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.DeleteMarketingAction
{
    public class DeleteMarketingActionHandler : IRequestHandler<DeleteMarketingActionRequest, DeleteMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<DeleteMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public DeleteMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<DeleteMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
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

            var now = DateTime.UtcNow;

            if (_options.Value.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))
            {
                try
                {
                    await _outlookSync.DeleteEventAsync(action.OutlookEventId, cancellationToken);
                    action.ClearOutlookLink();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to delete Outlook event {EventId} for MarketingAction {ActionId}; will retry",
                        action.OutlookEventId,
                        request.Id);
                    action.MarkOutlookFailed(ex.Message, now);
                }

                // Best-effort: persist Outlook sync status. A failure here is non-blocking.
                try
                {
                    await _repository.UpdateAsync(action, cancellationToken);
                    await _repository.SaveChangesAsync(cancellationToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx,
                        "Failed to persist Outlook sync status for MarketingAction {ActionId} before soft delete; continuing with delete",
                        request.Id);
                }
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
