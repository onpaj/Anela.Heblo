using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly string _containerName;

    public GetExpeditionDatesHandler(IExpeditionListArchiveBlobStore blobStore, IOptions<ExpeditionListArchiveOptions> options)
    {
        _blobStore = blobStore;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        var prefixes = await _blobStore.ListVirtualDirectoriesAsync(_containerName, cancellationToken);

        var dates = prefixes
            .Where(IsValidDatePrefix)
            .OrderByDescending(d => d, StringComparer.Ordinal)
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
