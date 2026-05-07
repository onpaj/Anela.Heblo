namespace Anela.Heblo.Application.Features.Leaflet.Services;

public interface ILeafletChunkSummarizer
{
    Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default);
}
