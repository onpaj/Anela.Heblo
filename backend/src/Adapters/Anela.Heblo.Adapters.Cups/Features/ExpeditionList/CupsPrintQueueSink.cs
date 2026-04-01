using Anela.Heblo.Application.Features.ExpeditionList.Services;

namespace Anela.Heblo.Adapters.Cups.Features.ExpeditionList;

public class CupsPrintQueueSink : IPrintQueueSink
{
    private readonly ICupsPrintingService _cupsPrintingService;

    public CupsPrintQueueSink(ICupsPrintingService cupsPrintingService)
    {
        _cupsPrintingService = cupsPrintingService;
    }

    public async Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        foreach (var filePath in filePaths)
        {
            await _cupsPrintingService.PrintAsync(filePath, cancellationToken: cancellationToken);
        }
    }
}
