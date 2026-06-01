using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot
{
    public class AddRootRequest : IRequest<AddRootResponse>
    {
        public string SharePointPath { get; set; } = null!;
        public string? DisplayName { get; set; }
        public string DriveId { get; set; } = null!;
    }
}
