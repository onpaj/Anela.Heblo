using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive.UseCases.ReprintExpeditionList;

public class ReprintExpeditionListHandler : IRequestHandler<ReprintExpeditionListRequest, ReprintExpeditionListResponse>
{
    private readonly IExpeditionListArchiveBlobStore _blobStore;
    private readonly IPrintQueueSink _cupsSink;
    private readonly ITemporaryFileAccessor _temporaryFileAccessor;
    private readonly string _containerName;

    // Prefers the keyed "cups" IPrintQueueSink (the physical CUPS printer, registered in
    // Cups/Combined print-sink modes) over whatever the ambient, non-keyed IPrintQueueSink
    // is configured to (which can be FileSystem, AzureBlob, Cups, or Combined depending on
    // ExpeditionList:PrintSink) -- reprints must always target the physical printer when one
    // is available, never the general expedition-list print flow's sink. Falls back to the
    // ambient sink only when no keyed "cups" registration exists (e.g. FileSystem in
    // development/test). This selection is expressed here via [FromKeyedServices] instead of
    // a manual IRequestHandler factory so it no longer depends on ExpeditionListArchiveModule
    // being registered after AddMediatR -- see issue #4330.
    //
    // cupsSink is declared last (with a `= null` default, not just a `?` nullable annotation)
    // because the built-in DI container's optional-parameter check for constructor injection
    // is based on ParameterInfo.HasDefaultValue, not on C#'s compile-time-only nullable
    // reference annotations -- without the explicit default, resolving this handler throws
    // when no keyed "cups" registration exists at all, instead of falling back to null. C#
    // itself requires an optional parameter to come after every required one (CS1737), which
    // is why cupsSink is last rather than adjacent to fallbackSink.
    public ReprintExpeditionListHandler(
        IExpeditionListArchiveBlobStore blobStore,
        IPrintQueueSink fallbackSink,
        ITemporaryFileAccessor temporaryFileAccessor,
        IOptions<ExpeditionListArchiveOptions> options,
        [FromKeyedServices("cups")] IPrintQueueSink? cupsSink = null)
    {
        _blobStore = blobStore;
        _cupsSink = cupsSink ?? fallbackSink;
        _temporaryFileAccessor = temporaryFileAccessor;
        _containerName = options.Value.BlobContainerName;
    }

    public async Task<ReprintExpeditionListResponse> Handle(ReprintExpeditionListRequest request, CancellationToken cancellationToken)
    {
        if (!BlobPathValidator.IsValid(request.BlobPath))
        {
            return ReprintExpeditionListResponse.Fail();
        }

        string? tempFile = null;
        try
        {
            await using var blobStream = await _blobStore.DownloadAsync(_containerName, request.BlobPath, cancellationToken);
            tempFile = await _temporaryFileAccessor.CreateFromStreamAsync(blobStream, ".pdf", cancellationToken);

            await _cupsSink.SendAsync(new[] { tempFile }, cancellationToken);
            return new ReprintExpeditionListResponse { Success = true };
        }
        finally
        {
            if (tempFile != null)
            {
                _temporaryFileAccessor.DeleteIfExists(tempFile);
            }
        }
    }
}
