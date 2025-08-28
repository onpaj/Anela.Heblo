using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Infrastructure;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Journal.Handlers
{
    public class CreateJournalTagHandler : IRequestHandler<CreateJournalTagRequest, CreateJournalTagResponse>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJournalTagRepository _tagRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<CreateJournalTagHandler> _logger;

        public CreateJournalTagHandler(
            IUnitOfWork unitOfWork,
            IJournalTagRepository tagRepository,
            ICurrentUserService currentUserService,
            ILogger<CreateJournalTagHandler> logger)
        {
            _unitOfWork = unitOfWork;
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

            // Using dispose pattern - SaveChangesAsync called automatically on dispose
            await using (_unitOfWork)
            {
                var tag = new JournalEntryTag
                {
                    Name = request.Name.Trim(),
                    Color = request.Color,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = currentUser.Id
                };

                var createdTag = await _tagRepository.AddAsync(tag, cancellationToken);

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
            // SaveChangesAsync is automatically called here when _unitOfWork is disposed
        }
    }
}