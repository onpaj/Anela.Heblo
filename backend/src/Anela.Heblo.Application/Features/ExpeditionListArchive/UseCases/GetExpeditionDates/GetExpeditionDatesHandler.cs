using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly string _containerName;

    public GetExpeditionDatesHandler(IBlobStorageService blobStorageService, IOptions<PrintPickingListOptions> options)
    {
        _blobStorageService = blobStorageService;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(_containerName, null, cancellationToken);

        var dates = blobs
            .Select(b => b.Name.Split('/')[0])
            .Where(IsValidDatePrefix)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        var totalCount = dates.Count;
        var pagedDates = dates
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new GetExpeditionDatesResponse
        {
            Dates = pagedDates,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static bool IsValidDatePrefix(string prefix)
    {
        return DateOnly.TryParseExact(prefix, "yyyy-MM-dd", out _);
    }
}
