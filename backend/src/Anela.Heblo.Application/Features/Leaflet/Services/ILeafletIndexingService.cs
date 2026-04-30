using Anela.Heblo.Domain.Features.Leaflet;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public interface ILeafletIndexingService
{
    /// <summary>
    /// Chunks, embeds, and persists <paramref name="text"/> as leaflet chunks.
    /// Sets <paramref name="document"/>.WordCount from the original text word count.
    /// </summary>
    Task IndexAsync(string text, LeafletDocument document, CancellationToken ct = default);
}
