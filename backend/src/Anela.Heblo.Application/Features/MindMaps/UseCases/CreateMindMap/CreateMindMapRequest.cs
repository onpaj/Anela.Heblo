using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapRequest : IRequest<CreateMindMapResponse>
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    [MaxLength(2000)]
    public string? Description { get; set; }
}
