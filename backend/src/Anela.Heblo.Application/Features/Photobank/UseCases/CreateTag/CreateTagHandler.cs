using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag
{
    public class CreateTagHandler : IRequestHandler<CreateTagRequest, CreateTagResponse>
    {
        private readonly IPhotobankRepository _repository;
        private readonly IPhotobankTagsCache _cache;

        public CreateTagHandler(IPhotobankRepository repository, IPhotobankTagsCache cache)
        {
            _repository = repository;
            _cache = cache;
        }

        public async Task<CreateTagResponse> Handle(CreateTagRequest request, CancellationToken cancellationToken)
        {
            var normalizedName = request.Name.Trim().ToLowerInvariant();

            // Two-step check-then-create is intentional; the TOCTOU race window is acceptable
            // for a single-user marketing tool. GetOrCreateTagAsync handles concurrent inserts atomically.
            var existing = await _repository.GetTagByNameAsync(normalizedName, cancellationToken);
            if (existing != null)
                return new CreateTagResponse { Id = existing.Id, Name = existing.Name, AlreadyExisted = true };

            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            if (tag is null)
                throw new InvalidOperationException($"GetOrCreateTagAsync returned null for '{normalizedName}'.");

            _cache.Invalidate();
            return new CreateTagResponse { Id = tag.Id, Name = tag.Name, AlreadyExisted = false };
        }
    }
}
