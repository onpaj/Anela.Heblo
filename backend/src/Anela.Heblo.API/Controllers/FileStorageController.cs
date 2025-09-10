using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Controller for file storage operations including Azure Blob Storage
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileStorageController : BaseApiController
{
    private readonly IMediator _mediator;

    public FileStorageController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Downloads a file from the specified URL and uploads it to Azure Blob Storage
    /// </summary>
    /// <param name="request">Request containing file URL, container name, and optional blob name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Information about the uploaded blob including its URL</returns>
    [HttpPost("download")]
    public async Task<ActionResult<DownloadFromUrlResponse>> DownloadFromUrl(
        [FromBody] DownloadFromUrlRequest request,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Processing file download and upload request from URL: {FileUrl} to container: {ContainerName}",
            request.FileUrl, request.ContainerName);

        var response = await _mediator.Send(request, cancellationToken);

        return HandleResponse(response);
    }
}