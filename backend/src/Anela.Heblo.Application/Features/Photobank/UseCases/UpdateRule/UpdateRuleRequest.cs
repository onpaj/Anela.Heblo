using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.UpdateRule
{
    public class UpdateRuleRequest : IRequest<UpdateRuleResponse>
    {
        public int Id { get; set; }
        public string PathPattern { get; set; } = null!;
        public string TagName { get; set; } = null!;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
