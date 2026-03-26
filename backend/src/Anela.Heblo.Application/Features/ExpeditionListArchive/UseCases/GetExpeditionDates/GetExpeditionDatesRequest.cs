using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesRequest : IRequest<GetExpeditionDatesResponse>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
