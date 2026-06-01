using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListRequest : IRequest<ReprintExpeditionListResponse>
{
    public string BlobPath { get; set; } = string.Empty;
}
