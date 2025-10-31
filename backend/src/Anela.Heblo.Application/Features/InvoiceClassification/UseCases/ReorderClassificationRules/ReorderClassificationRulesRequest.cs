using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ReorderClassificationRules;

public class ReorderClassificationRulesRequest : IRequest<ReorderClassificationRulesResponse>
{
    public List<Guid> RuleIds { get; set; } = new();
}