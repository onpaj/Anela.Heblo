using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.Handlers
{
    public class UpdateJournalEntryHandler : IRequestHandler<UpdateJournalEntryRequest, UpdateJournalEntryResponse>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJournalRepository _journalRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<UpdateJournalEntryHandler> _logger;

        public UpdateJournalEntryHandler(
            IUnitOfWork unitOfWork,
            IJournalRepository journalRepository,
            ICurrentUserService currentUserService,
            ILogger<UpdateJournalEntryHandler> logger)
        {
            _unitOfWork = unitOfWork;
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
                throw new UnauthorizedAccessException("User must be authenticated to update journal entries");
            }

            var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
            if (entry == null)
            {
                throw new InvalidOperationException($"Journal entry with ID {request.Id} not found");
            }

            // Check if user owns the entry (for now, allow all authenticated users to edit)
            // In production, you might want to restrict this to the original author

            var now = DateTime.UtcNow;

            // Update basic fields
            entry.Title = request.Title?.Trim();
            entry.Content = request.Content.Trim();
            entry.EntryDate = request.EntryDate.Date;
            entry.ModifiedAt = now;
            entry.ModifiedByUserId = currentUser.Id;

            // Update product associations (both products and families)
            entry.ProductAssociations.Clear();
            if (request.AssociatedProducts?.Any() == true)
            {
                foreach (var productIdentifier in request.AssociatedProducts.Distinct())
                {
                    // Try as full product code first, then as prefix
                    entry.AssociateWithProduct(productIdentifier);
                }
            }

            // Update tag assignments
            entry.TagAssignments.Clear();
            if (request.TagIds?.Any() == true)
            {
                foreach (var tagId in request.TagIds.Distinct())
                {
                    entry.AssignTag(tagId);
                }
            }

            await _journalRepository.UpdateAsync(entry, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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