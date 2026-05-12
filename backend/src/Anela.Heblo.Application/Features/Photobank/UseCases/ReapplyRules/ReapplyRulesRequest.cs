using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.ReapplyRules
{
    public class ReapplyRulesRequest : IRequest<ReapplyRulesResponse>
    {
        /// <summary>
        /// When set, only the tag produced by this rule is recomputed.
        /// When null, all Rule-sourced tags are recomputed.
        /// </summary>
        public int? RuleId { get; set; }
    }
}
