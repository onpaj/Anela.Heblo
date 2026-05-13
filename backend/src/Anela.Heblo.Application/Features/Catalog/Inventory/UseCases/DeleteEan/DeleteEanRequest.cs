using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;

public class DeleteEanRequest : IRequest<DeleteEanResponse>
{
    public int Id { get; set; }
}
