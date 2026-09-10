using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook
{
    public class ImportFromOutlookHandler : IRequestHandler<ImportFromOutlookRequest, ImportFromOutlookResponse>
    {
        private readonly IMarketingCalendarSyncService _syncService;
        private readonly ICurrentUserService _currentUserService;

        public ImportFromOutlookHandler(
            IMarketingCalendarSyncService syncService,
            ICurrentUserService currentUserService)
        {
            _syncService = syncService;
            _currentUserService = currentUserService;
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

            return await _syncService.SyncAsync(
                request.FromUtc,
                request.ToUtc,
                SyncActor.FromUser(currentUser),
                request.DryRun,
                cancellationToken);
        }
    }
}
