namespace Anela.Heblo.Domain.Features.MindMaps;

public interface IMindMapRepository
{
    /// <summary>
    /// Loads the map including Meetings (with transcripts). Versions are deliberately NOT
    /// loaded here — each version carries a full document Json blob, and this method backs
    /// the detail endpoint the frontend polls every 3s while a map is updating. Use
    /// <see cref="GetVersionSummariesAsync"/> for version metadata or
    /// <see cref="GetVersionAsync"/>/<see cref="GetForUpdateAsync"/> when the Json is actually needed.
    /// </summary>
    Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Loads the map including Meetings (with transcripts) AND Versions with their full Json —
    /// for the mind map update job, which needs the version history to number and append new
    /// snapshots. Not for read paths; see <see cref="GetByIdAsync"/>.
    /// </summary>
    Task<MindMap?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Metadata-only projection of a map's versions (no Json blob), newest first.</summary>
    Task<List<MindMapVersionSummary>> GetVersionSummariesAsync(Guid mindMapId, CancellationToken ct = default);

    /// <summary>Loads exactly one version, including its Json blob.</summary>
    Task<MindMapVersion?> GetVersionAsync(Guid mindMapId, int versionNumber, CancellationToken ct = default);

    /// <summary>Next version number to assign a new snapshot for this map (1 if none exist yet).</summary>
    Task<int> GetNextVersionNumberAsync(Guid mindMapId, CancellationToken ct = default);

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
