using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailRepository : IEcomailRepository
{
    private readonly ApplicationDbContext _context;

    public EcomailRepository(ApplicationDbContext context) => _context = context;

    // Tracked on purpose: the sync service mutates the returned entities in place.
    public async Task<Dictionary<int, EcomailCampaign>> GetCampaignsByIdAsync(CancellationToken cancellationToken = default)
        => await _context.EcomailCampaigns.ToDictionaryAsync(c => c.Id, cancellationToken);

    public async Task<Dictionary<int, EcomailPipeline>> GetPipelinesByIdAsync(CancellationToken cancellationToken = default)
        => await _context.EcomailPipelines.ToDictionaryAsync(p => p.Id, cancellationToken);

    public void AddCampaign(EcomailCampaign campaign) => _context.EcomailCampaigns.Add(campaign);

    public void AddPipeline(EcomailPipeline pipeline) => _context.EcomailPipelines.Add(pipeline);

    public Task<bool> SnapshotExistsAsync(int pipelineId, DateOnly capturedOn, CancellationToken cancellationToken = default)
        => _context.EcomailAutomationSnapshots
            .AnyAsync(s => s.PipelineId == pipelineId && s.CapturedOn == capturedOn, cancellationToken);

    public void AddSnapshot(EcomailAutomationSnapshot snapshot) => _context.EcomailAutomationSnapshots.Add(snapshot);

    public async Task<Dictionary<(int PipelineId, int Year, int Month), EcomailAutomationMonth>> GetAutomationMonthsAsync(
        CancellationToken cancellationToken = default)
        => await _context.EcomailAutomationMonths
            .ToDictionaryAsync(m => (m.PipelineId, m.Year, m.Month), cancellationToken);

    public void AddAutomationMonth(EcomailAutomationMonth month) => _context.EcomailAutomationMonths.Add(month);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
