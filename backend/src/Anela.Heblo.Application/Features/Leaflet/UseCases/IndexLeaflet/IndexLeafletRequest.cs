using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletRequest : IRequest<IndexLeafletResponse>
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }
}
