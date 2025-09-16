using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;

public class UpdateManufactureOrderStatusRequest : IRequest<UpdateManufactureOrderStatusResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    public ManufactureOrderState NewState { get; set; }

    public string? ChangeReason { get; set; }
}