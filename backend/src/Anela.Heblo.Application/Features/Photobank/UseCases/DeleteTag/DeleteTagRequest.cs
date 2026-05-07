using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteTag
{
    public class DeleteTagRequest : IRequest<DeleteTagResponse>
    {
        public int Id { get; set; }
    }
}
