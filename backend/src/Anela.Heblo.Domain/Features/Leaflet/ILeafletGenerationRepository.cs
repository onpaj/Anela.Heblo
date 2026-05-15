namespace Anela.Heblo.Domain.Features.Leaflet;

public interface ILeafletGenerationRepository
{
    Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken);
    Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool descending,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken);
    Task<UpdateFeedbackResult> UpdateFeedbackAsync(
        Guid generationId,
        int? precisionScore,
        int? styleScore,
        string? comment,
        CancellationToken cancellationToken);
}
