using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Products_Catalog)]
[ApiController]
[Route("api/label-identification")]
public class LabelIdentificationController : BaseApiController
{
    // Current Android flagships routinely produce 8-20 MB JPEGs; iOS 48 MP output reaches
    // 5-10 MB. An attribute argument must be a compile-time constant, so this cap cannot
    // read LabelIdentificationOptions — it is the sole enforcement point for upload size.
    private const int MaxUploadBytes = 25 * 1024 * 1024;

    private readonly IMediator _mediator;

    public LabelIdentificationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Identifies a product from a photo of its etiquette. Labels print only the INCI
    /// composition, which identifies a product FAMILY — size variants share artwork text,
    /// so a family with two sizes returns both variants for the operator to choose from.
    /// </summary>
    [HttpPost("identify")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<IdentifyLabelResponse>> Identify(
        IFormFile? photo,
        CancellationToken ct = default)
    {
        if (!IsValidPhoto(photo))
        {
            return BadRequest(new IdentifyLabelResponse(ErrorCodes.LabelPhotoMissingOrInvalid));
        }

        await using var stream = photo!.OpenReadStream();
        var response = await _mediator.Send(new IdentifyLabelRequest
        {
            PhotoStream = stream,
            ContentType = photo.ContentType ?? string.Empty,
            SizeBytes = photo.Length,
        }, ct);

        return HandleResponse(response);
    }

    private static bool IsValidPhoto(IFormFile? photo)
    {
        if (photo is null || photo.Length == 0)
        {
            return false;
        }

        return !string.IsNullOrEmpty(photo.ContentType) &&
               photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
