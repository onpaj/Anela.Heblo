using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddRule
{
    public class AddRuleHandler : IRequestHandler<AddRuleRequest, AddRuleResponse>
    {
        private readonly IPhotobankRepository _repository;

        public AddRuleHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<AddRuleResponse> Handle(AddRuleRequest request, CancellationToken cancellationToken)
        {
            var rule = new TagRule
            {
                PathPattern = request.PathPattern.Trim(),
                TagName = request.TagName.Trim().ToLowerInvariant(),
                IsActive = true,
                SortOrder = request.SortOrder,
            };

            var created = await _repository.AddRuleAsync(rule, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new AddRuleResponse { Id = created.Id };
        }
    }
}
