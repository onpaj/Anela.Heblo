using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.UseCases.CreateJournalEntry
{
    public class CreateJournalEntryHandler : IRequestHandler<CreateJournalEntryRequest, CreateJournalEntryResponse>
    {
        private readonly IJournalRepository _journalRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateJournalEntryHandler> _logger;

        public CreateJournalEntryHandler(
            IJournalRepository journalRepository,
            ICurrentUserService currentUserService,
            ILogger<CreateJournalEntryHandler> logger)
        {
            _journalRepository = journalRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<CreateJournalEntryResponse> Handle(
            CreateJournalEntryRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                return new CreateJournalEntryResponse(ErrorCodes.UnauthorizedJournalAccess, new Dictionary<string, string>
                {
                    { "resource", "journal_entry" }
                });
            }

            var userId = currentUser.Id;
            var now = DateTime.UtcNow;

            var entry = new JournalEntry
            {
                Title = request.Title?.Trim(),
                Content = request.Content.Trim(),
                EntryDate = request.EntryDate.Date,
                CreatedAt = now,
                ModifiedAt = now,
                CreatedByUserId = userId,
                CreatedByUsername = currentUser.Name ?? "Unknown User"
            };

            // Associate products (can be full product codes or prefixes/families)
            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var productIdentifier in request.AssociatedProducts.Distinct())
                {
                    // Try as full product code first, then as prefix
                    entry.AssociateWithProduct(productIdentifier);
                }
            }

            // Assign tags
            if (request.TagIds?.Any() == true)
            {
                foreach (var tagId in request.TagIds.Distinct())
                {
                    entry.AssignTag(tagId);
                }
            }

            var createdEntry = await _journalRepository.AddAsync(entry, cancellationToken);
            await _journalRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Journal entry {EntryId} created by user {UserId}",
                createdEntry.Id,
                userId);

            return new CreateJournalEntryResponse
            {
                Id = createdEntry.Id,
                CreatedAt = createdEntry.CreatedAt
            };
        }
    }
}