using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.DownloadExpeditionList;

public class DownloadExpeditionListRequest : IRequest<DownloadExpeditionListResponse>
{
    public string BlobPath { get; set; } = string.Empty;
}
