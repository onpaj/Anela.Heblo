using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.SaveMindMapDocument;

public class SaveMindMapDocumentRequest : IRequest<SaveMindMapDocumentResponse>
{
    public Guid Id { get; set; }

    [Required]
    public string DocumentJson { get; set; } = null!;
}
