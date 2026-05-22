using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;

public class UploadMaterialDocumentHandler
    : IRequestHandler<UploadMaterialDocumentRequest, UploadDocumentResponse>
{
    private readonly ICatalogDocumentsStorage _storage;
    private readonly CatalogDocumentsOptions _options;
    private readonly ILogger<UploadMaterialDocumentHandler> _logger;

    public UploadMaterialDocumentHandler(
        ICatalogDocumentsStorage storage,
        IOptions<CatalogDocumentsOptions> options,
        ILogger<UploadMaterialDocumentHandler> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadMaterialDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!request.UploadAsIs)
        {
            var validationError = ValidateStructuredUpload(request);
            if (validationError != null) return validationError;
        }

        var prefix = $"{request.ProductCode}__";
        var folder = await _storage.FindFolderAsync(
            _options.Materials.DriveId, _options.Materials.BasePath, prefix,
            allowMultiple: false, cancellationToken);

        if (folder.Status == FolderStatus.NotFound)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderNotFound,
                new Dictionary<string, string> { ["prefix"] = prefix, ["basePath"] = _options.Materials.BasePath });

        if (folder.Status == FolderStatus.MultipleMatches)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentFolderMultipleMatches,
                new Dictionary<string, string> { ["prefix"] = prefix });

        var filename = request.UploadAsIs
            ? request.OriginalFilename
            : BuildFilename(request);

        _logger.LogInformation("Uploading material document {Filename} for product {ProductCode}", filename, request.ProductCode);

        var uploadedName = await _storage.UploadFileAsync(
            _options.Materials.DriveId, folder.FolderId, filename,
            request.FileStream, request.ContentType, request.SizeBytes, cancellationToken);

        return new UploadDocumentResponse { UploadedFilename = uploadedName };
    }

    private static UploadDocumentResponse? ValidateStructuredUpload(UploadMaterialDocumentRequest request)
    {
        var type = MaterialDocumentTypes.All.FirstOrDefault(t =>
            string.Equals(t.Code, request.DocumentTypeCode, StringComparison.OrdinalIgnoreCase));

        if (type is null)
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentInvalidTypeCode,
                new Dictionary<string, string> { ["code"] = request.DocumentTypeCode });

        if (type.LotRequired && string.IsNullOrWhiteSpace(request.Lot))
            return new UploadDocumentResponse(ErrorCodes.CatalogDocumentLotRequired,
                new Dictionary<string, string> { ["type"] = request.DocumentTypeCode });

        return null;
    }

    private static string BuildFilename(UploadMaterialDocumentRequest request)
    {
        var ext = Path.GetExtension(request.OriginalFilename);
        return MaterialFilenameBuilder.Build(request.DocumentTypeCode, request.Lot, request.CommonName, ext);
    }
}
