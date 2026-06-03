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

namespace Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction
{
    public class CreateMarketingActionHandler : IRequestHandler<CreateMarketingActionRequest, CreateMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptionsMonitor<MarketingCalendarOptions> _options;

        public CreateMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<CreateMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptionsMonitor<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<CreateMarketingActionResponse> Handle(
            CreateMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new CreateMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var now = DateTime.UtcNow;

            var action = new MarketingAction(
                title: request.Title,
                description: request.Description,
                actionType: request.ActionType,
                startDate: request.StartDate,
                endDate: request.EndDate,
                createdByUserId: currentUser.Id,
                createdByUsername: currentUser.Name,
                utcNow: now);

            if (request.AssociatedProducts?.Any() == true)
                foreach (var product in request.AssociatedProducts.Distinct())
                    action.AssociateWithProduct(product);

            if (request.FolderLinks?.Any() == true)
                foreach (var link in request.FolderLinks)
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);

            string? outlookEventId = null;

            if (_options.CurrentValue.PushEnabled)
            {
                try
                {
                    outlookEventId = await _outlookSync.CreateEventAsync(action, cancellationToken);
                    action.MarkOutlookSynced(outlookEventId, now);
                }
                catch (OutlookCalendarSyncException ex)
                {
                    _logger.LogError(ex, "Outlook CreateEvent failed for new MarketingAction; user {UserId}", currentUser.Id);
                    return OutlookError(ex);
                }
            }

            await _repository.AddAsync(action, cancellationToken);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx,
                    "DB save failed after Outlook create; compensating Outlook event {EventId}", outlookEventId);

                if (outlookEventId != null)
                {
                    try
                    {
                        await _outlookSync.DeleteEventAsync(outlookEventId, cancellationToken);
                        _logger.LogWarning("Compensating delete of Outlook event {EventId} succeeded", outlookEventId);
                    }
                    catch (Exception compEx)
                    {
                        _logger.LogError(compEx,
                            "Compensating delete of Outlook event {EventId} also failed — event orphaned",
                            outlookEventId);
                    }
                }

                return new CreateMarketingActionResponse(ErrorCodes.DatabaseError);
            }

            _logger.LogInformation("MarketingAction {ActionId} created by user {UserId}", action.Id, currentUser.Id);

            return new CreateMarketingActionResponse { Id = action.Id, CreatedAt = action.CreatedAt };
        }

        private static CreateMarketingActionResponse OutlookError(OutlookCalendarSyncException ex) =>
            ex.StatusCode == HttpStatusCode.Forbidden
                ? new CreateMarketingActionResponse(ErrorCodes.MarketingCalendarAccessDenied)
                : new CreateMarketingActionResponse(ErrorCodes.MarketingCalendarSyncFailed);
    }
}
