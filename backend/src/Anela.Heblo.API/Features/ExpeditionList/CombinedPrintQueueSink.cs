using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.API.Features.ExpeditionList;

internal sealed class CombinedPrintQueueSink : IPrintQueueSink
{
    private readonly IPrintQueueSink _azureSink;
    private readonly IPrintQueueSink _cupsSink;

    public CombinedPrintQueueSink(
        [FromKeyedServices("azure")] IPrintQueueSink azureSink,
        [FromKeyedServices("cups")] IPrintQueueSink cupsSink)
    {
        _azureSink = azureSink;
        _cupsSink = cupsSink;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        var paths = filePaths.ToList();
        await _azureSink.SendAsync(paths, cancellationToken);
        await _cupsSink.SendAsync(paths, cancellationToken);
    }
}
