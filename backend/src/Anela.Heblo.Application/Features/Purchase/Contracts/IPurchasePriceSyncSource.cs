namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public interface IPurchasePriceSyncSource
{
    /// <summary>
    /// Every Material and Goods item that has a ceník row, with its current purchase price and
    /// today's average stock price. Throws when the ceník or stock cannot be loaded.
    /// </summary>
    Task<IReadOnlyList<PurchasePriceSyncCandidate>> GetCandidatesAsync(CancellationToken cancellationToken);
}
