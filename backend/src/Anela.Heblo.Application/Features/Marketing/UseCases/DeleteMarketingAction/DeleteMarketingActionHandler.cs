using System.Net;
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
        private readonly IOptionsMonitor<MarketingCalendarOptions> _options;

        public DeleteMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<DeleteMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptionsMonitor<MarketingCalendarOptions> options)
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
                return new DeleteMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var now = DateTime.UtcNow;

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new DeleteMarketingActionResponse(ErrorCodes.MarketingActionNotFound,
                    new Dictionary<string, string> { { "actionId", request.Id.ToString() } });
            }

            if (_options.CurrentValue.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))
            {
                try
                {
                    await _outlookSync.DeleteEventAsync(action.OutlookEventId, cancellationToken);
                }
                catch (OutlookCalendarSyncException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogInformation(
                        "Outlook event {EventId} already deleted (404); proceeding with soft-delete. ActionId={ActionId} UserId={UserId}",
                        action.OutlookEventId, request.Id, currentUser.Id);
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex,
                        "Outlook DeleteEvent failed for MarketingAction {ActionId}; user {UserId}",
                        request.Id, currentUser.Id);
                    return OutlookError(ex);
                }
            }

            action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User", now);

            try
            {
                await _repository.UpdateAsync(action, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DB soft-delete failed after Outlook delete for MarketingAction {ActionId}; Outlook event {EventId} already deleted — DB row still present",
                    request.Id, action.OutlookEventId);
                return new DeleteMarketingActionResponse(ErrorCodes.DatabaseError);
            }

            _logger.LogInformation("MarketingAction {ActionId} deleted by user {UserId}", request.Id, currentUser.Id);

            return new DeleteMarketingActionResponse { Id = request.Id };
        }

        private static DeleteMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new DeleteMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new DeleteMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
