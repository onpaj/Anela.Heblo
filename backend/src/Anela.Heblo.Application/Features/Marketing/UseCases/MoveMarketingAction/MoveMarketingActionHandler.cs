using System;
using System.Collections.Generic;
using System.Net;
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

namespace Anela.Heblo.Application.Features.Marketing.UseCases.MoveMarketingAction
{
    public class MoveMarketingActionHandler : IRequestHandler<MoveMarketingActionRequest, MoveMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<MoveMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptionsMonitor<MarketingCalendarOptions> _options;

        public MoveMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<MoveMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptionsMonitor<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<MoveMarketingActionResponse> Handle(
            MoveMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new MoveMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new MoveMarketingActionResponse(ErrorCodes.MarketingActionNotFound,
                    new Dictionary<string, string> { { "actionId", request.Id.ToString() } });
            }

            var now = DateTime.UtcNow;

            action.UpdateDetails(
                title: action.Title,
                description: action.Description,
                actionType: action.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                modifiedByUserId: currentUser.Id,
                modifiedByUsername: currentUser.Name,
                utcNow: now);

            if (_options.CurrentValue.PushEnabled && !string.IsNullOrEmpty(action.OutlookEventId))
            {
                try
                {
                    await _outlookSync.UpdateEventAsync(action, cancellationToken);
                    action.MarkOutlookSynced(action.OutlookEventId, now);
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex,
                        "Outlook push failed for MarketingAction {ActionId}; user {UserId}",
                        request.Id, currentUser.Id);
                    return OutlookError(ex);
                }
            }

            try
            {
                await _repository.UpdateAsync(action, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DB save failed after Outlook update for MarketingAction {ActionId}; Outlook event {EventId} may now be out of sync",
                    action.Id, action.OutlookEventId);
                return new MoveMarketingActionResponse(ErrorCodes.DatabaseError);
            }

            _logger.LogInformation(
                "MarketingAction {ActionId} moved by user {UserId}",
                action.Id, currentUser.Id);

            return new MoveMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt };
        }

        private static MoveMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new MoveMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new MoveMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
