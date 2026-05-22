using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;

public class ListMaterialDocumentsHandler : IRequestHandler<ListMaterialDocumentsRequest, ListCatalogDocumentsResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<ListMaterialDocumentsHandler> _logger;

    public ListMaterialDocumentsHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<ListMaterialDocumentsHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ListCatalogDocumentsResponse> Handle(
        ListMaterialDocumentsRequest request, CancellationToken cancellationToken)
    {
        var prefix = $"{request.ProductCode}__";
        var driveId = _options.Materials.DriveId;
        var basePath = _options.Materials.BasePath;

        var folder = await _storage.FindFolderAsync(driveId, basePath, prefix, allowMultiple: false, cancellationToken);

        if (folder.Status != FolderStatus.Found)
        {
            _logger.LogInformation("Material folder not found for product {ProductCode} under {BasePath} (status={Status})",
                request.ProductCode, basePath, folder.Status);

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
