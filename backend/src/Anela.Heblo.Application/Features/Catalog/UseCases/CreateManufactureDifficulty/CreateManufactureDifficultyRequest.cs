using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.CreateManufactureDifficulty;

public class CreateManufactureDifficultyRequest : IRequest<CreateManufactureDifficultyResponse>
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Difficulty value must be non-negative")]
    public int DifficultyValue { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}