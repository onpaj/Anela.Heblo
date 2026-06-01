using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetClassificationRules;

public class GetClassificationRulesRequest : IRequest<GetClassificationRulesResponse>
{
    public bool IncludeInactive { get; set; } = false;
}