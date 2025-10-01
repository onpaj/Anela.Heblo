using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;

public class RecalculateProductWeightRequest : IRequest<RecalculateProductWeightResponse>
{
    public string? ProductCode { get; set; }
}