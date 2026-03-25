using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();
}
