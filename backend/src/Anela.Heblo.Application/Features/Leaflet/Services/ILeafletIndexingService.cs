using Anela.Heblo.Domain.Features.Leaflet;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public interface ILeafletIndexingService
{
    Task IndexAsync(string text, LeafletDocument document, CancellationToken ct = default);
}
