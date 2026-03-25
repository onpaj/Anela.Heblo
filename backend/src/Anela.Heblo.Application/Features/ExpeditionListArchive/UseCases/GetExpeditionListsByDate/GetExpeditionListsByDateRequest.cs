using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateRequest : IRequest<GetExpeditionListsByDateResponse>
{
    public string Date { get; set; } = string.Empty;
}
