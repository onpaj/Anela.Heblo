namespace Anela.Heblo.Domain.Features.DataQuality;

[Flags]
public enum StockWriteBackMismatch
{
    None = 0,
    OperationFailed = 1,
    OperationStuck = 2,
    StockTakingErrored = 4
}
