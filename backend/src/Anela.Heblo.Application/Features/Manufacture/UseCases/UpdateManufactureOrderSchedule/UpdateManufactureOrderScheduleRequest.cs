using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;

public class UpdateManufactureOrderScheduleRequest : IRequest<UpdateManufactureOrderScheduleResponse>
{
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// New scheduled date for semi-product manufacturing
    /// </summary>
    public DateOnly? SemiProductPlannedDate { get; set; }

    /// <summary>
    /// New scheduled date for product manufacturing  
    /// </summary>
    public DateOnly? ProductPlannedDate { get; set; }

    /// <summary>
    /// Reason for the schedule change (for audit trail)
    /// </summary>
    public string? ChangeReason { get; set; } = "Schedule updated via drag & drop";
}