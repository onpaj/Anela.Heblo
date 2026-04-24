using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.Contracts
{
    public class AddRootRequest : IRequest<AddRootResponse>
    {
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
    }

    public class AddRootResponse : BaseResponse
    {
        public int Id { get; set; }
    }
}
