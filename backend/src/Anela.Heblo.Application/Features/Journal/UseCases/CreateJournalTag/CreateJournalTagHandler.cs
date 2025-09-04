using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.UseCases.CreateJournalTag
{
    public class CreateJournalTagHandler : IRequestHandler<CreateJournalTagRequest, CreateJournalTagResponse>
    {
        private readonly IJournalTagRepository _tagRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateJournalTagHandler> _logger;

        public CreateJournalTagHandler(
            IJournalTagRepository tagRepository,
            ICurrentUserService currentUserService,
            ILogger<CreateJournalTagHandler> logger)
        {
            _tagRepository = tagRepository;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<CreateJournalTagResponse> Handle(
            CreateJournalTagRequest request,
            CancellationToken cancellationToken)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.Id))
            {
                throw new UnauthorizedAccessException("User must be authenticated to create tags");
            }

            var tag = new JournalEntryTag
            {
                Name = request.Name.Trim(),
                Color = request.Color,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = currentUser.Id
            };

            var createdTag = await _tagRepository.AddAsync(tag, cancellationToken);
            await _tagRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Journal tag {TagId} created by user {UserId}",
                createdTag.Id,
                currentUser.Id);

            return new CreateJournalTagResponse
            {
                Id = createdTag.Id,
                Name = createdTag.Name,
                Color = createdTag.Color
            };
        }
    }
}