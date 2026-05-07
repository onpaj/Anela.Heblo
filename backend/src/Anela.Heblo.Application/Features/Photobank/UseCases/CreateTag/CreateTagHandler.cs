using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag
{
    public class CreateTagHandler : IRequestHandler<CreateTagRequest, CreateTagResponse>
    {
        private readonly IPhotobankRepository _repository;

        public CreateTagHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<CreateTagResponse> Handle(CreateTagRequest request, CancellationToken cancellationToken)
        {
            var normalizedName = request.Name.Trim().ToLowerInvariant();

            var existing = await _repository.GetTagByNameAsync(normalizedName, cancellationToken);
            if (existing != null)
                return new CreateTagResponse { Id = existing.Id, Name = existing.Name, AlreadyExisted = true };

            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);
            return new CreateTagResponse { Id = tag!.Id, Name = tag.Name, AlreadyExisted = false };
        }
    }
}
