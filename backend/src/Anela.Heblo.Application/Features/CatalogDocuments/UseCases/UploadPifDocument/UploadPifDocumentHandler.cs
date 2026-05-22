using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;

public class UploadPifDocumentHandler : IRequestHandler<UploadPifDocumentRequest, UploadDocumentResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<UploadPifDocumentHandler> _logger;

    public UploadPifDocumentHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<UploadPifDocumentHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadPifDocumentRequest request, CancellationToken cancellationToken)
    {
        var shortCode = request.ProductCode.Length >= 6
            ? request.ProductCode[..6]
            : request.ProductCode;
        var prefix = $"{shortCode}__";

        var folder = await _storage.FindFolderAsync(
            _options.PIF.DriveId, _options.PIF.BasePath, prefix,
            allowMultiple: true, cancellationToken);

        if (folder.Status == FolderStatus.NotFound)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderNotFound,
                new Dictionary<string, string> { ["prefix"] = prefix, ["basePath"] = _options.PIF.BasePath });

        _logger.LogInformation("Uploading PIF document {Filename} for product {ProductCode}", request.OriginalFilename, request.ProductCode);

        var uploadedName = await _storage.UploadFileAsync(
            _options.PIF.DriveId, folder.FolderId, request.OriginalFilename,
            request.FileStream, request.ContentType, request.SizeBytes, cancellationToken);

        return new UploadDocumentResponse { UploadedFilename = uploadedName };
    }
}
