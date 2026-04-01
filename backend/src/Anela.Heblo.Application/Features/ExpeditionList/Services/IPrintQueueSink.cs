namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
