using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private const string ContainerName = "expedition-lists";
    private readonly IBlobStorageService _blobStorageService;

    public GetExpeditionDatesHandler(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var blobs = await _blobStorageService.ListBlobsAsync(ContainerName, null, cancellationToken);

        var dates = blobs
            .Select(b => b.Name.Split('/')[0])
            .Where(d => IsValidDatePrefix(d))
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
