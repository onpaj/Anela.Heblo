using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderSchedule;

public class UpdateManufactureOrderScheduleRequest : IRequest<UpdateManufactureOrderScheduleResponse>
{
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// New scheduled date for manufacturing (unified for both single-phase and multi-phase)
    /// </summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>
    /// Reason for the schedule change (for audit trail)
    /// </summary>
    public string? ChangeReason { get; set; } = "Schedule updated via drag & drop";
}