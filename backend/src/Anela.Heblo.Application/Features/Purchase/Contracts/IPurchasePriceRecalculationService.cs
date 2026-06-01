namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceRecalculationService
{
    Task RecalculatePurchasePriceAsync(int bomId, CancellationToken cancellationToken);
}
