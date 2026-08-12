using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionRequest : IRequest<RestoreMindMapVersionResponse>
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
}
