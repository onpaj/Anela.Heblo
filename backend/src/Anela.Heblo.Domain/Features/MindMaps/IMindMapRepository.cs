namespace Anela.Heblo.Domain.Features.MindMaps;

public interface IMindMapRepository
{
    /// <summary>Loads the map including Meetings (with transcripts) and Versions.</summary>
    Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads all maps including Meetings (for counts), newest first.</summary>
    Task<List<MindMap>> GetListAsync(CancellationToken ct = default);

    Task AddAsync(MindMap map, CancellationToken ct = default);

    Task DeleteAsync(MindMap map, CancellationToken ct = default);

    /// <summary>
    /// Marks a map Failed via a direct update, bypassing the change tracker entirely.
    /// Safe to call after a failed <see cref="SaveChangesAsync"/>, whose failure may leave
    /// poisoned (still-tracked) entities behind that a subsequent tracked save would resubmit.
    /// </summary>
    Task SetFailedAsync(Guid mindMapId, string lastError, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
