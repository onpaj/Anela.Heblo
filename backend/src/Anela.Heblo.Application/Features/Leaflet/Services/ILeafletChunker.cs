using Anela.Heblo.Domain.Features.Leaflet;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public interface ILeafletChunker
{
    IReadOnlyList<LeafletChunk> Chunk(string text, Guid documentId);
}
