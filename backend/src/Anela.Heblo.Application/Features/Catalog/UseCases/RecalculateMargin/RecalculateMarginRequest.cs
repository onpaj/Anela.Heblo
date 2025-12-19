using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateMargin;

public class RecalculateMarginRequest : IRequest<RecalculateMarginResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public int MonthsBack { get; set; } = 13;
}
