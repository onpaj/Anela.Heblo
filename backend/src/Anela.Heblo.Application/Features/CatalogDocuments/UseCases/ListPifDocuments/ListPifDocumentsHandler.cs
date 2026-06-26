using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;

public class ListPifDocumentsHandler : IRequestHandler<ListPifDocumentsRequest, ListCatalogDocumentsResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<ListPifDocumentsHandler> _logger;

    public ListPifDocumentsHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<ListPifDocumentsHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ListCatalogDocumentsResponse> Handle(
        ListPifDocumentsRequest request, CancellationToken cancellationToken)
    {
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";
        var driveId = _options.PIF.DriveId;
        var basePath = _options.PIF.BasePath;

        var folder = await _storage.FindFolderAsync(driveId, basePath, prefix, allowMultiple: true, cancellationToken);

        if (folder.Status != FolderStatus.Found)
        {
            _logger.LogInformation("PIF folder not found for product {ProductCode} under {BasePath}",
                request.ProductCode, basePath);

            return new ListCatalogDocumentsResponse
            {
                FolderStatus = folder.Status,
                ExpectedPrefix = prefix,
                BasePath = basePath,
                Files = [],
            };
        }

        var files = await _storage.ListFilesAsync(driveId, folder.FolderId, cancellationToken);

        return new ListCatalogDocumentsResponse
        {
            FolderStatus = FolderStatus.Found,
            ExpectedPrefix = prefix,
            BasePath = basePath,
            Files = files,
        };
    }
}
