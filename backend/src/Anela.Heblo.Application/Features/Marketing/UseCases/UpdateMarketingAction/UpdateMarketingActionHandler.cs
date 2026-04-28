using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Anela.Heblo.Application.Features.Marketing.UseCases.UpdateMarketingAction
{
    public class UpdateMarketingActionHandler : IRequestHandler<UpdateMarketingActionRequest, UpdateMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<UpdateMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public UpdateMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<UpdateMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _logger = logger;
            _outlookSync = outlookSync;
            _options = options;
        }

        public async Task<UpdateMarketingActionResponse> Handle(
            UpdateMarketingActionRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new UpdateMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess, new Dictionary<string, string>
                {
                    { "resource", "marketing_action" },
                });
            }

            var action = await _repository.GetByIdAsync(request.Id, cancellationToken);
            if (action == null)
            {
                return new UpdateMarketingActionResponse(ErrorCodes.MarketingActionNotFound, new Dictionary<string, string>
                {
                    { "actionId", request.Id.ToString() },
                });
            }

            var now = DateTime.UtcNow;

            action.Title = request.Title.Trim();
            action.Description = request.Description?.Trim();
            action.ActionType = request.ActionType;
            action.StartDate = request.StartDate;
            action.EndDate = request.EndDate;
            action.ModifiedAt = now;
            action.ModifiedByUserId = currentUser.Id;
            action.ModifiedByUsername = currentUser.Name ?? "Unknown User";

            // Replace product associations
            action.ProductAssociations.Clear();
            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var product in request.AssociatedProducts.Distinct())
                {
                    action.AssociateWithProduct(product);
                }
            }

            // Replace folder links
            action.FolderLinks.Clear();
            if (request.FolderLinks?.Any() == true)
            {
                foreach (var link in request.FolderLinks)
                {
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);
                }
            }

            await _repository.UpdateAsync(action, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            if (_options.Value.PushEnabled)
            {
                try
                {
                    if (!string.IsNullOrEmpty(action.OutlookEventId))
                    {
                        await _outlookSync.UpdateEventAsync(action, cancellationToken);
                        action.MarkOutlookSynced(action.OutlookEventId, now);
                    }
                    else
                    {
                        var eventId = await _outlookSync.CreateEventAsync(action, cancellationToken);
                        action.MarkOutlookSynced(eventId, now);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync MarketingAction {ActionId} to Outlook; will retry", action.Id);
                    action.MarkOutlookFailed(ex.Message, now);
                }

                // Best-effort: persist Outlook sync status. A failure here is non-blocking.
                try
                {
                    await _repository.SaveChangesAsync(cancellationToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx,
                        "Outlook sync status for MarketingAction {ActionId} could not be persisted to the database",
                        action.Id);
                }
            }

            _logger.LogInformation(
                "MarketingAction {ActionId} updated by user {UserId}",
                action.Id,
                currentUser.Id);

            return new UpdateMarketingActionResponse
            {
                Id = action.Id,
                ModifiedAt = action.ModifiedAt,
            };
        }
    }
}
