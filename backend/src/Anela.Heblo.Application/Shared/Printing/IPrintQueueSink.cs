namespace Anela.Heblo.Application.Shared.Printing;

public interface IPrintQueueSink
{
    Task SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);
}
