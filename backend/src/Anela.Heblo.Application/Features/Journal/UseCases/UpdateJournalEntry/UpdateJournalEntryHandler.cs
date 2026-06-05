using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.UseCases.UpdateJournalEntry
{
    public class UpdateJournalEntryHandler : IRequestHandler<UpdateJournalEntryRequest, UpdateJournalEntryResponse>
    {
        private readonly IJournalRepository _journalRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<UpdateJournalEntryHandler> _logger;

        public UpdateJournalEntryHandler(
            IJournalRepository journalRepository,
            ICurrentUserService currentUserService,
            ILogger<UpdateJournalEntryHandler> logger)
        {
            _journalRepository = journalRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<UpdateJournalEntryResponse> Handle(
            UpdateJournalEntryRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new UpdateJournalEntryResponse(ErrorCodes.UnauthorizedJournalAccess, new Dictionary<string, string>
                {
                    { "resource", "journal_entry" }
                });
            }

            var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null)
            {
                return new UpdateJournalEntryResponse(ErrorCodes.JournalEntryNotFound, new Dictionary<string, string>
                {
                    { "entryId", request.Id.ToString() }
                });
            }

            // Check if user owns the entry (for now, allow all authenticated users to edit)
            // In production, you might want to restrict this to the original author

            entry.Update(
                request.Title,
                request.Content,
                request.EntryDate,
                currentUser.Id,
                currentUser.Name ?? "Unknown User");

            entry.ReplaceProductAssociations(request.AssociatedProducts);
            entry.ReplaceTagAssignments(request.TagIds);

            await _journalRepository.UpdateAsync(entry, cancellationToken);
            await _journalRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Journal entry {EntryId} updated by user {UserId}",
                entry.Id,
                currentUser.Id);

            return new UpdateJournalEntryResponse
            {
                Id = entry.Id,
                ModifiedAt = entry.ModifiedAt
            };
        }
    }
}