using Anela.Heblo.Domain.Features.FileStorage;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.GetExpeditionDates;

public class GetExpeditionDatesHandler : IRequestHandler<GetExpeditionDatesRequest, GetExpeditionDatesResponse>
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<GetExpeditionDatesHandler> _logger;

    public GetExpeditionDatesHandler(IBlobStorageService blobStorageService, ILogger<GetExpeditionDatesHandler> logger)
    {
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<GetExpeditionDatesResponse> Handle(GetExpeditionDatesRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching expedition dates, page {Page}, pageSize {PageSize}", request.Page, request.PageSize);

        var blobs = await _blobStorageService.ListBlobsAsync(ExpeditionListArchiveConstants.ContainerName, cancellationToken: cancellationToken);

        var dates = blobs
            .Select(b => b.Name.Contains('/') ? b.Name[..b.Name.IndexOf('/')] : null)
            .Where(d => d != null && DateOnly.TryParseExact(d, "yyyy-MM-dd", out _))
            .Select(d => d!)
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
            PageSize = request.PageSize,
        };
    }
}
