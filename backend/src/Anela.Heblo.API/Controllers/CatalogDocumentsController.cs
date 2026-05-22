using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.GetMaterialDocumentTypes;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListPifDocuments;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadPifDocument;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/catalog-documents")]
public class CatalogDocumentsController : BaseApiController
{
    private readonly IMediator _mediator;

    public CatalogDocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("materials/{productCode}")]
    public async Task<ActionResult<ListCatalogDocumentsResponse>> ListMaterialDocuments(
        string productCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListMaterialDocumentsRequest { ProductCode = productCode }, ct);
        return HandleResponse(result);
    }

    [HttpGet("pif/{productCode}")]
    public async Task<ActionResult<ListCatalogDocumentsResponse>> ListPifDocuments(
        string productCode, CancellationToken ct)
    {
        var result = await _mediator.Send(new ListPifDocumentsRequest { ProductCode = productCode }, ct);
        return HandleResponse(result);
    }

    [HttpGet("material-document-types")]
    public async Task<ActionResult<GetMaterialDocumentTypesResponse>> GetMaterialDocumentTypes(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMaterialDocumentTypesRequest(), ct);
        return HandleResponse(result);
    }

    [HttpPost("materials/{productCode}")]
    [Authorize(Policy = AuthorizationConstants.Policies.CatalogDocumentsUpload)]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<UploadDocumentResponse>> UploadMaterialDocument(
        string productCode,
        IFormFile? file,
        [FromForm] string documentTypeCode = "",
        [FromForm] string lot = "",
        [FromForm] string commonName = "",
        [FromForm] bool uploadAsIs = false,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadMaterialDocumentRequest
        {
            ProductCode = productCode,
            OriginalFilename = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            FileStream = stream,
            DocumentTypeCode = documentTypeCode,
            Lot = lot,
            CommonName = string.IsNullOrWhiteSpace(commonName) ? Path.GetFileNameWithoutExtension(file.FileName) : commonName,
            UploadAsIs = uploadAsIs,
        }, ct);

        return HandleResponse(result);
    }

    [HttpPost("pif/{productCode}")]
    [Authorize(Policy = AuthorizationConstants.Policies.CatalogDocumentsUpload)]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<UploadDocumentResponse>> UploadPifDocument(
        string productCode,
        IFormFile? file,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadPifDocumentRequest
        {
            ProductCode = productCode,
            OriginalFilename = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            FileStream = stream,
        }, ct);

        return HandleResponse(result);
    }
}
