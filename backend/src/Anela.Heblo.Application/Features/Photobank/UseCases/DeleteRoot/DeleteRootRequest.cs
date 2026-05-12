using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.DeleteRoot
{
    public class DeleteRootRequest : IRequest<DeleteRootResponse>
    {
        public int Id { get; set; }
    }
}
