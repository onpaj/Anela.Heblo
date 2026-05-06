using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddRule
{
    public class AddRuleRequest : IRequest<AddRuleResponse>
    {
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public int SortOrder { get; set; }
    }
}
