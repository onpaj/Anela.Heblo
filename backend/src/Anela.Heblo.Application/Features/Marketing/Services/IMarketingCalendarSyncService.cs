using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// Mirrors the Outlook group calendar into Heblo marketing actions for a date window.
    /// Outlook is the source of truth; Heblo-only actions (no OutlookEventId) are never touched.
    /// </summary>
    public interface IMarketingCalendarSyncService
    {
        Task<ImportFromOutlookResponse> SyncAsync(
            DateTime fromUtc,
            DateTime toUtc,
            SyncActor actor,
            bool dryRun,
            CancellationToken cancellationToken);
    }
}
