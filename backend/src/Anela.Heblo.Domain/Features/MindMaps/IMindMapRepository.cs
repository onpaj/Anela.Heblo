namespace Anela.Heblo.Domain.Features.MindMaps;

public interface IMindMapRepository
{
    /// <summary>Loads the map including Meetings (with transcripts) and Versions.</summary>
    Task<MindMap?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads all maps including Meetings (for counts), newest first.</summary>
    Task<List<MindMap>> GetListAsync(CancellationToken ct = default);

    Task AddAsync(MindMap map, CancellationToken ct = default);

    Task DeleteAsync(MindMap map, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
