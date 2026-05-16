using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;

public class ExplainSummaryRequest : IRequest<ExplainSummaryResponse>
{
    public Guid TranscriptId { get; set; }

    [Required]
    public string SelectedText { get; set; } = null!;
}
