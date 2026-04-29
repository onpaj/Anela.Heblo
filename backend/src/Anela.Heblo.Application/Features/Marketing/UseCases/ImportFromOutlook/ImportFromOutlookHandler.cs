using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        private static readonly Regex ScriptStyleRegex = new(
            @"<(script|style)[^>]*>.*?</(script|style)>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new(
            @"<[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        private readonly IMarketingActionRepository _repository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IMarketingCategoryMapper _mapper;
        private readonly ILogger<ImportFromOutlookHandler> _logger;

        public ImportFromOutlookHandler(
            IMarketingActionRepository repository,
            ICurrentUserService currentUserService,
            IOutlookCalendarSync outlookSync,
            IMarketingCategoryMapper mapper,
            ILogger<ImportFromOutlookHandler> logger)
        {
            _repository = repository;
            _currentUserService = currentUserService;
            _outlookSync = outlookSync;
            _mapper = mapper;
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
            var unmappedAccumulator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                if (knownEventIds.Contains(evt.Id))
                {
                    response.Skipped++;
                    response.Items.Add(new ImportedItemDto
                    {
                        OutlookEventId = evt.Id,
                        Subject = evt.Subject,
                        Status = ImportStatus.Skipped,
                    });
                    continue;
                }

                try
                {
                    var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
                    var action = BuildAction(evt, currentUser, utcNow, mapping.ActionType);

                    if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0)
                    {
                        foreach (var name in mapping.UnmappedCategories)
                        {
                            unmappedAccumulator.Add(name);
                        }
                    }

                    if (!request.DryRun)
                    {
                        var created = await _repository.AddAsync(action, cancellationToken);
                        await _repository.SaveChangesAsync(cancellationToken);

                        response.Created++;
                        response.Items.Add(new ImportedItemDto
                        {
                            OutlookEventId = evt.Id,
                            Subject = evt.Subject,
                            Status = ImportStatus.Created,
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
                            Status = ImportStatus.WouldCreate,
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
                        Status = ImportStatus.Failed,
                        Error = ex.Message,
                    });
                }
            }

            response.UnmappedCategories = unmappedAccumulator.ToList();

            if (unmappedAccumulator.Count > 0)
            {
                _logger.LogInformation(
                    "Marketing import completed with {Count} unmapped Outlook categor{Plural}: {Categories}",
                    unmappedAccumulator.Count,
                    unmappedAccumulator.Count == 1 ? "y" : "ies",
                    string.Join(", ", unmappedAccumulator));
            }

            return response;
        }

        private static MarketingAction BuildAction(
            OutlookEventDto evt,
            CurrentUser currentUser,
            DateTime utcNow,
            MarketingActionType actionType)
        {
            var title = (evt.Subject ?? string.Empty).Length > 200
                ? evt.Subject[..200]
                : evt.Subject ?? string.Empty;

            var rawDescription = StripHtml(evt.BodyText);
            var description = rawDescription?.Length > 5000
                ? rawDescription[..5000]
                : rawDescription;

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
            var result = ScriptStyleRegex.Replace(html, string.Empty);

            // Remove remaining tags
            result = HtmlTagRegex.Replace(result, string.Empty);

            // Decode HTML entities and collapse whitespace
            result = System.Net.WebUtility.HtmlDecode(result);
            result = WhitespaceRegex.Replace(result, " ").Trim();

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
