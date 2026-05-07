using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.RemovePhotoTag
{
    public class RemovePhotoTagRequest : IRequest<RemovePhotoTagResponse>
    {
        public int PhotoId { get; set; }
        public int TagId { get; set; }
    }
}
