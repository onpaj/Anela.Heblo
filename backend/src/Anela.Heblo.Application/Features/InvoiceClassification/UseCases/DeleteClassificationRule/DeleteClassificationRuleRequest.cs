using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.DeleteClassificationRule;

public class DeleteClassificationRuleRequest : IRequest<DeleteClassificationRuleResponse>
{
    public Guid Id { get; set; }
}