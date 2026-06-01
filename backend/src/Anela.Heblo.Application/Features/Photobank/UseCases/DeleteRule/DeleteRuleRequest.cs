using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRule
{
    public class DeleteRuleRequest : IRequest<DeleteRuleResponse>
    {
        public int Id { get; set; }
    }
}
