using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public class MarketingCalendarSyncService : IMarketingCalendarSyncService
    {
        private readonly IMarketingActionRepository _repository;
        private readonly IOutlookCalendarSync _outlookSync;
        private readonly IMarketingCategoryMapper _mapper;
        private readonly ILogger<MarketingCalendarSyncService> _logger;

        public MarketingCalendarSyncService(
            IMarketingActionRepository repository,
            IOutlookCalendarSync outlookSync,
            IMarketingCategoryMapper mapper,
            ILogger<MarketingCalendarSyncService> logger)
        {
            _repository = repository;
            _outlookSync = outlookSync;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ImportFromOutlookResponse> SyncAsync(
            DateTime fromUtc,
            DateTime toUtc,
            SyncActor actor,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            var events = await _outlookSync.ListEventsAsync(fromUtc, toUtc, cancellationToken);

            var eventIds = events.Select(e => e.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            var existingActions = await _repository.GetByOutlookEventIdsAsync(eventIds, cancellationToken);
            var existingByEventId = existingActions
                .GroupBy(a => a.OutlookEventId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var run = new SyncRun(actor, dryRun, DateTime.UtcNow);

            foreach (var evt in events)
            {
                try
                {
                    await ProcessEventAsync(evt, existingByEventId, run, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown/cancellation aborts the run; reporting it as a per-event
                    // failure would fabricate a wall of failures for a healthy calendar.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to import Outlook event {EventId} (subject: {Subject})",
                        evt.Id,
                        evt.Subject);
                    run.AddFailed(evt.Id, evt.Subject, ex.Message);
                }
            }

            await ReconcileOrphansAsync(fromUtc, toUtc, eventIds, run, cancellationToken);

            await PersistAsync(run, cancellationToken);
            run.ReportStaged();

            var response = run.Response;
            response.UnmappedCategories = run.UnmappedCategories.ToList();

            if (run.UnmappedCategories.Count > 0)
            {
                _logger.LogInformation(
                    "Marketing import completed with {Count} unmapped Outlook categor{Plural}: {Categories}",
                    run.UnmappedCategories.Count,
                    run.UnmappedCategories.Count == 1 ? "y" : "ies",
                    string.Join(", ", run.UnmappedCategories));
            }

            return response;
        }

        private async Task ProcessEventAsync(
            OutlookEventDto evt,
            IReadOnlyDictionary<string, MarketingAction> existingByEventId,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
            HarvestUnmappedCategories(mapping, run);

            if (existingByEventId.TryGetValue(evt.Id, out var existing))
            {
                await StageUpdateAsync(existing, evt, mapping.ActionType, run, cancellationToken);
                return;
            }

            await StageCreateAsync(evt, mapping.ActionType, run, cancellationToken);
        }

        private static void HarvestUnmappedCategories(CategoryMappingResult mapping, SyncRun run)
        {
            if (mapping.MatchedCategory is null && mapping.UnmappedCategories.Count > 0)
            {
                foreach (var name in mapping.UnmappedCategories)
                {
                    run.UnmappedCategories.Add(name);
                }
            }
        }

        private async Task StageUpdateAsync(
            MarketingAction existing,
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var needsRestore = existing.IsDeleted
                && existing.DeletedByUserId == SyncActor.SystemUserId;

            if (!needsRestore && !OutlookEventImportMapper.HasChanges(existing, evt, actionType))
            {
                run.AddSkipped(evt);
                return;
            }

            if (run.DryRun)
            {
                run.AddWouldUpdate(evt);
                return;
            }

            if (needsRestore)
            {
                // Only deletions made by the sync itself are reversible; a person deleting
                // an imported action in Heblo keeps it hidden even if Outlook still has it.
                existing.Restore(run.Actor.UserId, run.Actor.Username, run.UtcNow);
            }

            OutlookEventImportMapper.ApplyChanges(existing, evt, actionType, run.Actor, run.UtcNow);

            // Staging only: AddAsync/UpdateAsync just attach to the change tracker and do
            // not hit the database. Every real persistence failure therefore surfaces in
            // the single SaveChangesAsync in PersistAsync and fails the whole batch — the
            // per-event try/catch in SyncAsync isolates mapping/Graph errors, not save errors.
            await _repository.UpdateAsync(existing, cancellationToken);
            run.PendingUpdates.Add((existing, evt));
        }

        private async Task StageCreateAsync(
            OutlookEventDto evt,
            MarketingActionType actionType,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var action = OutlookEventImportMapper.BuildAction(evt, run.Actor, run.UtcNow, actionType);

            if (run.DryRun)
            {
                run.AddWouldCreate(evt);
                return;
            }

            await _repository.AddAsync(action, cancellationToken);
            run.PendingCreates.Add((action, evt));
        }

        /// <summary>
        /// Actions in the window whose event was not returned by calendarView are
        /// confirmed one by one: 404 ⇒ deleted in Outlook ⇒ soft-delete here;
        /// found ⇒ moved outside the window ⇒ treat as a normal update.
        /// </summary>
        private async Task ReconcileOrphansAsync(
            DateTime fromUtc,
            DateTime toUtc,
            IReadOnlyCollection<string> fetchedEventIds,
            SyncRun run,
            CancellationToken cancellationToken)
        {
            var fetched = new HashSet<string>(fetchedEventIds, StringComparer.OrdinalIgnoreCase);
            var windowActions = await _repository.GetSyncedInWindowAsync(fromUtc, toUtc, cancellationToken);
            var orphans = windowActions.Where(a => !fetched.Contains(a.OutlookEventId!)).ToList();

            foreach (var orphan in orphans)
            {
                try
                {
                    await ReconcileOrphanAsync(orphan, run, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to reconcile marketing action {ActionId} (Outlook event {EventId})",
                        orphan.Id,
                        orphan.OutlookEventId);
                    run.AddFailed(orphan.OutlookEventId!, orphan.Title, ex.Message);
                }
            }
        }

        private async Task ReconcileOrphanAsync(MarketingAction orphan, SyncRun run, CancellationToken cancellationToken)
        {
            var evt = await _outlookSync.GetEventAsync(orphan.OutlookEventId!, cancellationToken);

            if (evt is not null)
            {
                var mapping = _mapper.MapToActionType(evt.Categories ?? Array.Empty<string>());
                HarvestUnmappedCategories(mapping, run);
                await StageUpdateAsync(orphan, evt, mapping.ActionType, run, cancellationToken);
                return;
            }

            if (run.DryRun)
            {
                run.AddWouldDelete(orphan);
                return;
            }

            // Attributed to the sync, never to run.Actor: a reconciliation delete is made by
            // the sync regardless of who triggered the run. Stamping the human who pressed
            // Import would make it indistinguishable from a manual Heblo delete, and the
            // restore rule below (DeletedByUserId == SystemUserId) would then hide the
            // action forever if its Outlook event came back.
            orphan.SoftDelete(SyncActor.SystemUserId, SyncActor.System.Username, run.UtcNow);

            // Staging only — same as StageCreateAsync/StageUpdateAsync: UpdateAsync does not
            // touch the database, so a save failure fails the whole batch in PersistAsync.
            // This orphan's try/catch isolates Graph lookup errors, not save errors.
            await _repository.UpdateAsync(orphan, cancellationToken);
            run.PendingDeletes.Add(orphan);
        }

        // Persistence is deferred until the loop completes so that a single
        // SaveChangesAsync covers the whole run. Saving per-event used to leave the
        // shared DbContext dirty after a failed save, poisoning every subsequent
        // event in the run (and costing N round-trips).
        private async Task PersistAsync(SyncRun run, CancellationToken cancellationToken)
        {
            if (run.DryRun || !run.HasPendingWrites)
            {
                return;
            }

            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // The batch is atomic: if the single save fails, none of the staged
                // writes were persisted. Report them all as failed instead of
                // claiming success for unwritten rows.
                _logger.LogError(ex,
                    "Failed to persist Outlook import batch of {Count} action(s); no changes were saved",
                    run.PendingCount);

                // Detach before reporting: a failed SaveChanges leaves the staged entities
                // tracked as Added/Modified, which would poison any later SaveChangesAsync
                // in this same DI scope (a known trap in this codebase).
                _repository.DetachRange(run.StagedActions);

                run.FailAllPending(ex.Message);
            }
        }

        /// <summary>Mutable bookkeeping for one SyncAsync call.</summary>
        private sealed class SyncRun
        {
            public SyncRun(SyncActor actor, bool dryRun, DateTime utcNow)
            {
                Actor = actor;
                DryRun = dryRun;
                UtcNow = utcNow;
            }

            public SyncActor Actor { get; }
            public bool DryRun { get; }
            public DateTime UtcNow { get; }
            public ImportFromOutlookResponse Response { get; } = new();
            public HashSet<string> UnmappedCategories { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<(MarketingAction action, OutlookEventDto evt)> PendingCreates { get; } = new();
            public List<(MarketingAction action, OutlookEventDto evt)> PendingUpdates { get; } = new();
            public List<MarketingAction> PendingDeletes { get; } = new();

            public IEnumerable<MarketingAction> StagedActions =>
                PendingCreates.Select(p => p.action)
                    .Concat(PendingUpdates.Select(p => p.action))
                    .Concat(PendingDeletes);

            public bool HasPendingWrites => PendingCount > 0;
            public int PendingCount => PendingCreates.Count + PendingUpdates.Count + PendingDeletes.Count;

            public void AddSkipped(OutlookEventDto evt)
            {
                Response.Skipped++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Skipped));
            }

            public void AddWouldCreate(OutlookEventDto evt)
            {
                Response.Created++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.WouldCreate));
            }

            public void AddWouldUpdate(OutlookEventDto evt)
            {
                Response.Updated++;
                Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.WouldUpdate));
            }

            public void AddFailed(string eventId, string subject, string error)
            {
                Response.Failed++;
                Response.Items.Add(Item(eventId, subject, ImportStatus.Failed, error: error));
            }

            public void AddWouldDelete(MarketingAction action)
            {
                Response.Deleted++;
                Response.Items.Add(Item(action.OutlookEventId!, action.Title, ImportStatus.WouldDelete, actionId: action.Id));
            }

            public void FailAllPending(string error)
            {
                foreach (var (_, evt) in PendingCreates.Concat(PendingUpdates))
                {
                    AddFailed(evt.Id, evt.Subject, error);
                }

                PendingCreates.Clear();
                PendingUpdates.Clear();

                foreach (var action in PendingDeletes)
                {
                    AddFailed(action.OutlookEventId!, action.Title, error);
                }

                PendingDeletes.Clear();
            }

            /// <summary>Turns the surviving staged writes into Created/Updated items.</summary>
            public void ReportStaged()
            {
                foreach (var (action, evt) in PendingCreates)
                {
                    Response.Created++;
                    Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Created, actionId: action.Id));
                }

                foreach (var (action, evt) in PendingUpdates)
                {
                    Response.Updated++;
                    Response.Items.Add(Item(evt.Id, evt.Subject, ImportStatus.Updated, actionId: action.Id));
                }

                foreach (var action in PendingDeletes)
                {
                    Response.Deleted++;
                    Response.Items.Add(Item(action.OutlookEventId!, action.Title, ImportStatus.Deleted, actionId: action.Id));
                }
            }

            private static ImportedItemDto Item(
                string eventId,
                string subject,
                string status,
                string? error = null,
                int? actionId = null)
            {
                return new ImportedItemDto
                {
                    OutlookEventId = eventId,
                    Subject = subject,
                    Status = status,
                    Error = error,
                    CreatedActionId = actionId,
                };
            }
        }
    }
}
