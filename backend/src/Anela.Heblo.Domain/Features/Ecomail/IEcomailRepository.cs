namespace Anela.Heblo.Domain.Features.Ecomail;

public interface IEcomailRepository
{
    Task<Dictionary<int, EcomailCampaign>> GetCampaignsByIdAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<int, EcomailPipeline>> GetPipelinesByIdAsync(CancellationToken cancellationToken = default);

    void AddCampaign(EcomailCampaign campaign);
    void AddPipeline(EcomailPipeline pipeline);

    Task<bool> SnapshotExistsAsync(int pipelineId, DateOnly capturedOn, CancellationToken cancellationToken = default);
    void AddSnapshot(EcomailAutomationSnapshot snapshot);

    Task<Dictionary<(int PipelineId, int Year, int Month), EcomailAutomationMonth>> GetAutomationMonthsAsync(
        CancellationToken cancellationToken = default);
    void AddAutomationMonth(EcomailAutomationMonth month);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
