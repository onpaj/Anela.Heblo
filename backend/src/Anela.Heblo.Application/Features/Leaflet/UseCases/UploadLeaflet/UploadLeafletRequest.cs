using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;

public class UploadLeafletRequest : IRequest<UploadLeafletResponse>
{
    public Stream FileStream { get; set; } = default!;
    public string Filename { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long FileSizeBytes { get; set; }
}
