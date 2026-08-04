using MediatR;

namespace Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;

public class IdentifyLabelRequest : IRequest<IdentifyLabelResponse>
{
    public Stream PhotoStream { get; set; } = Stream.Null;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
