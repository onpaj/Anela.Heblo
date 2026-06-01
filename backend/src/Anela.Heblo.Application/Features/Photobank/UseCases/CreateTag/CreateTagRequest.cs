using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.CreateTag
{
    public class CreateTagRequest : IRequest<CreateTagResponse>
    {
        public string Name { get; set; } = null!;
    }
}
