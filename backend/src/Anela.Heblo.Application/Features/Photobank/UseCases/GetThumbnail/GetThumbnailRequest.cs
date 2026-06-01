using Anela.Heblo.Application.Features.Photobank.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail
{
    public class GetThumbnailRequest : IRequest<GetThumbnailResponse>
    {
        public int Id { get; set; }
        public ThumbnailSize Size { get; set; }
    }
}
