using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderRequest : IRequest<UpdateManufactureOrderResponse>
{
    [Required]
    public int Id { get; set; }

    public DateOnly? SemiProductPlannedDate { get; set; }

    public DateOnly? ProductPlannedDate { get; set; }

    public string? ResponsiblePerson { get; set; }

    public string? ErpOrderNumberSemiproduct { get; set; }

    public string? ErpOrderNumberProduct { get; set; }

    public UpdateManufactureOrderSemiProductRequest? SemiProduct { get; set; }

    public List<UpdateManufactureOrderProductRequest> Products { get; set; } = new();

    public string? NewNote { get; set; }

    public bool? ManualActionRequired { get; set; }
}