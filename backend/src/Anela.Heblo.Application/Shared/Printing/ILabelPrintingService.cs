namespace Anela.Heblo.Application.Shared.Printing;

public interface ILabelPrintingService
{
    Task PrintZplAsync(string zpl, CancellationToken cancellationToken = default);
}
