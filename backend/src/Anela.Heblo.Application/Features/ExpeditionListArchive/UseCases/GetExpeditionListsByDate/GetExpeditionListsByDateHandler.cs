using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionListsByDate;

public class GetExpeditionListsByDateHandler : IRequestHandler<GetExpeditionListsByDateRequest, GetExpeditionListsByDateResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public GetExpeditionListsByDateHandler(IBlobStorageService blobStorageService, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionListsByDateResponse> Handle(GetExpeditionListsByDateRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
        {
            return new GetExpeditionListsByDateResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.InvalidFormat,
                Params = new Dictionary<string, string>
                {
                    { "Field", "Date" },
                    { "ExpectedFormat", "yyyy-MM-dd" }
                }
            };
        }

        var blobs = await _blobStorageService.ListBlobsAsync(_containerName, request.Date, cancellationToken);

        var items = blobs
            .Where(b => b.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .Select(b => new ExpeditionListItemDto
            {
                BlobPath = b.Name,
                FileName = b.FileName,
                ListId = Path.GetFileNameWithoutExtension(b.FileName),
                CreatedOn = b.CreatedOn,
                ContentLength = b.ContentLength
            })
            .ToList();

        return new GetExpeditionListsByDateResponse { Items = items };
    }
}
