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
            var existingByEventId = existingActions
                .GroupBy(a => a.OutlookEventId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var utcNow = DateTime.UtcNow;
            var response = new ImportFromOutlookResponse();
            var unmappedAccumulator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                try
                {
                    var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());

                    if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0)
                    {
                        foreach (var name in mapping.UnmappedCategories)
                        {
                            unmappedAccumulator.Add(name);
                        }
                    }

                    if (existingByEventId.TryGetValue(evt.Id, out var existing))
                    {
                        if (!OutlookEventImportMapper.HasChanges(existing, evt, mapping.ActionType))
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

                        OutlookEventImportMapper.ApplyChanges(existing, evt, mapping.ActionType, currentUser, utcNow);

                        if (!request.DryRun)
                        {
                            await _repository.UpdateAsync(existing, cancellationToken);
                            await _repository.SaveChangesAsync(cancellationToken);

                            response.Updated++;
                            response.Items.Add(new ImportedItemDto
                            {
                                OutlookEventId = evt.Id,
                                Subject = evt.Subject,
                                Status = ImportStatus.Updated,
                                CreatedActionId = existing.Id,
                            });
                        }
                        else
                        {
                            response.Updated++;
                            response.Items.Add(new ImportedItemDto
                            {
                                OutlookEventId = evt.Id,
                                Subject = evt.Subject,
                                Status = ImportStatus.WouldUpdate,
                            });
                        }
                    }
                    else
                    {
                        var action = OutlookEventImportMapper.BuildAction(evt, currentUser, utcNow, mapping.ActionType);

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

    }
}
