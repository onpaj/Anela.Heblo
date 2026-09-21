using Anela.Heblo.Domain.Features.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioRepository : IPricingScenarioRepository
{
    private readonly ApplicationDbContext _context;

    public PricingScenarioRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<List<PricingScenario>> GetAllAsync(CancellationToken ct = default)
        => _context.PricingScenarios
            // Items is what GetPricingScenariosHandler counts for EditedProductCount.
            // AsNoTracking disables navigation fix-up and no lazy-loading proxies are
            // configured, so without this Include every scenario in the dropdown
            // reported "(0)" no matter how many rows it actually held.
            .Include(s => s.Items)
            .AsNoTracking()
            .OrderByDescending(s => s.ModifiedAt)
            .ToListAsync(ct);

    public Task<PricingScenario?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(PricingScenario scenario, CancellationToken ct = default)
    {
        _context.PricingScenarios.Add(scenario);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PricingScenario scenario, CancellationToken ct = default)
    {
        // Replace the item set wholesale rather than diffing: a scenario is small, and
        // adding a child with an explicit PK to a tracked parent's collection makes EF emit
        // an UPDATE affecting zero rows instead of an INSERT.
        var existing = await _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == scenario.Id, ct);

        if (existing is null) return;

        existing.Name = scenario.Name;
        existing.Description = scenario.Description;
        existing.ModifiedAt = scenario.ModifiedAt;
        existing.FilterJson = scenario.FilterJson;

        _context.PricingScenarioItems.RemoveRange(existing.Items);
        foreach (var item in scenario.Items)
        {
            existing.Items.Add(new PricingScenarioItem
            {
                ProductCode = item.ProductCode,
                Price = item.Price,
                MaterialCost = item.MaterialCost,
                ManufacturingCost = item.ManufacturingCost,
                ForecastQuantity = item.ForecastQuantity,
                BaselinePrice = item.BaselinePrice,
                BaselineMaterialCost = item.BaselineMaterialCost,
                BaselineManufacturingCost = item.BaselineManufacturingCost,
                BaselineQuantity = item.BaselineQuantity
            });
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Load and Remove rather than ExecuteDelete: the EF InMemory provider used by the
        // unit tests throws on ExecuteDelete.
        var scenario = await _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (scenario is null) return;

        _context.PricingScenarios.Remove(scenario);
        await _context.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsByNameAsync(string name, Guid? excludingId, CancellationToken ct = default)
        => _context.PricingScenarios
            .AnyAsync(s => s.Name == name && (excludingId == null || s.Id != excludingId), ct);
}
