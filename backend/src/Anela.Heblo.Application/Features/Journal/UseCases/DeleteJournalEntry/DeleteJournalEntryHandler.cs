using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.UseCases.DeleteJournalEntry
{
    public class DeleteJournalEntryHandler : IRequestHandler<DeleteJournalEntryRequest, Unit>
    {
        private readonly IJournalRepository _journalRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<DeleteJournalEntryHandler> _logger;

        public DeleteJournalEntryHandler(
            IJournalRepository journalRepository,
            ICurrentUserService currentUserService,
            ILogger<DeleteJournalEntryHandler> logger)
        {
            _journalRepository = journalRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<Unit> Handle(
            DeleteJournalEntryRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                throw new UnauthorizedAccessException("User must be authenticated to delete journal entries");
            }

            var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null)
            {
                throw new InvalidOperationException($"Journal entry with ID {request.Id} not found");
            }

            // Check if user owns the entry (for now, allow all authenticated users to delete)
            // In production, you might want to restrict this to the original author

            await _journalRepository.DeleteSoftAsync(request.Id, currentUser.Id, cancellationToken);

            _logger.LogInformation(
                "Journal entry {EntryId} deleted by user {UserId}",
                request.Id,
                currentUser.Id);

            return Unit.Value;
        }
    }
}