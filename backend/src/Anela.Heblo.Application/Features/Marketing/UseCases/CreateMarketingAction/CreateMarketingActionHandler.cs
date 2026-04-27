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

namespace Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction
{
    public class CreateMarketingActionHandler : IRequestHandler<CreateMarketingActionRequest, CreateMarketingActionResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateMarketingActionHandler> _logger;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IOptions<MarketingCalendarOptions> _options;

        public CreateMarketingActionHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            ILogger<CreateMarketingActionHandler> logger,
            IOutlookCalendarSync outlookSync,
            IOptions<MarketingCalendarOptions> options)
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
                return new CreateMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess, new Dictionary<string, string>
                {
                    { "resource", "marketing_action" },
                });
            }

            var now = DateTime.UtcNow;

            var action = new MarketingAction
            {
                Title = request.Title.Trim(),
                Description = request.Description?.Trim(),
                ActionType = request.ActionType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = currentUser.Id,
                CreatedByUsername = currentUser.Name ?? "Unknown User",
            };

            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var product in request.AssociatedProducts.Distinct())
                {
                    action.AssociateWithProduct(product);
                }
            }

            if (request.FolderLinks?.Any() == true)
            {
                foreach (var link in request.FolderLinks)
                {
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType);
                }
            }

            var created = await _repository.AddAsync(action, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            if (_options.Value.PushEnabled)
            {
                try
                {
                    var eventId = await _outlookSync.CreateEventAsync(created, cancellationToken);
                    created.MarkOutlookSynced(eventId, now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync MarketingAction {ActionId} to Outlook; will retry", created.Id);
                    created.MarkOutlookFailed(ex.Message, now);
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
                        created.Id);
                }
            }

            _logger.LogInformation(
                "MarketingAction {ActionId} created by user {UserId}",
                created.Id,
                currentUser.Id);

            return new CreateMarketingActionResponse
            {
                Id = created.Id,
                CreatedAt = created.CreatedAt,
            };
        }
    }
}
