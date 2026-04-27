using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook
{
    public class ImportFromOutlookHandler : IRequestHandler<ImportFromOutlookRequest, ImportFromOutlookResponse>
    {
        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly ILogger<ImportFromOutlookHandler> _logger;

        public ImportFromOutlookHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            IOutlookCalendarSync outlookSync,
            ILogger<ImportFromOutlookHandler> logger)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _outlookSync = outlookSync;
            _logger = logger;
        }

        public async Task<ImportFromOutlookResponse> Handle(
            ImportFromOutlookRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new ImportFromOutlookResponse(
                    ErrorCodes.UnauthorizedMarketingAccess,
                    new Dictionary<string, string> { { "resource", "marketing_action" } });
            }

            var events = await _outlookSync.ListEventsAsync(request.FromUtc, request.ToUtc, cancellationToken);

            var eventIds = events.Select(e => e.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var existingActions = await _repository.GetByOutlookEventIdsAsync(eventIds, cancellationToken);
            var knownEventIds = new HashSet<string>(
                existingActions.Select(a => a.OutlookEventId!),
                StringComparer.OrdinalIgnoreCase);

            var utcNow = DateTime.UtcNow;
            var response = new ImportFromOutlookResponse();

            foreach (var evt in events)
            {
                if (knownEventIds.Contains(evt.Id))
                {
                    response.Skipped++;
                    response.Items.Add(new ImportedItemDto
                    {
                        OutlookEventId = evt.Id,
                        Subject = evt.Subject,
                        Status = "Skipped",
                    });
                    continue;
                }

                try
                {
                    var action = BuildAction(evt, currentUser, utcNow);

                    if (!request.DryRun)
                    {
                        var created = await _repository.AddAsync(action, cancellationToken);
                        await _repository.SaveChangesAsync(cancellationToken);

                        response.Created++;
                        response.Items.Add(new ImportedItemDto
                        {
                            OutlookEventId = evt.Id,
                            Subject = evt.Subject,
                            Status = "Created",
                            CreatedActionId = created.Id,
                        });
                    }
                    else
                    {
                        response.Created++;
                        response.Items.Add(new ImportedItemDto
                        {
                            OutlookEventId = evt.Id,
                            Subject = evt.Subject,
                            Status = "Created",
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to import Outlook event {EventId} (subject: {Subject})",
                        evt.Id,
                        evt.Subject);

                    response.Failed++;
                    response.Items.Add(new ImportedItemDto
                    {
                        OutlookEventId = evt.Id,
                        Subject = evt.Subject,
                        Status = "Failed",
                        Error = ex.Message,
                    });
                }
            }

            return response;
        }

        private static MarketingAction BuildAction(OutlookEventDto evt, CurrentUser currentUser, DateTime utcNow)
        {
            var title = (evt.Subject ?? string.Empty).Length > 200
                ? evt.Subject[..200]
                : evt.Subject ?? string.Empty;

            var rawDescription = StripHtml(evt.BodyText);
            var description = rawDescription?.Length > 5000
                ? rawDescription[..5000]
                : rawDescription;

            var category = evt.Categories.FirstOrDefault();
            var actionType = Enum.TryParse<MarketingActionType>(category, ignoreCase: true, out var parsed)
                ? parsed
                : MarketingActionType.General;

            var endDate = evt.EndUtc == DateTime.MinValue || evt.EndUtc == evt.StartUtc
                ? (DateTime?)null
                : evt.EndUtc;

            var action = new MarketingAction
            {
                Title = title,
                Description = description,
                ActionType = actionType,
                StartDate = evt.StartUtc,
                EndDate = endDate,
                CreatedAt = utcNow,
                ModifiedAt = utcNow,
                CreatedByUserId = currentUser.Id!,
                CreatedByUsername = currentUser.Name ?? "Unknown User",
            };

            action.MarkOutlookSynced(evt.Id, utcNow);

            return action;
        }

        private static string? StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            // Remove script/style blocks
            var result = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<(script|style)[^>]*>.*?</(script|style)>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            // Remove remaining tags
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", string.Empty);

            // Decode HTML entities and collapse whitespace
            result = System.Net.WebUtility.HtmlDecode(result);
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
