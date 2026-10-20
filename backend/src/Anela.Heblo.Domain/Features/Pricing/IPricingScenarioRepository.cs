namespace Anela.Heblo.Domain.Features.Pricing;

public interface IPricingScenarioRepository
{
    Task<List<PricingScenario>> GetAllAsync(CancellationToken ct = default);
    Task<PricingScenario?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PricingScenario scenario, CancellationToken ct = default);
    Task UpdateAsync(PricingScenario scenario, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludingId, CancellationToken ct = default);
}
