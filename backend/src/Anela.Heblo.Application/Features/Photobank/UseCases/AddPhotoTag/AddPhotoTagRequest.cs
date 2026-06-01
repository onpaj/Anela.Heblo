using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag
{
    public class AddPhotoTagRequest : IRequest<AddPhotoTagResponse>
    {
        public int PhotoId { get; set; }
        public string TagName { get; set; } = null!;
    }
}
