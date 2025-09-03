using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class UpdateManufactureDifficultyRequest : IRequest<UpdateManufactureDifficultyResponse>
{
    [Required]
    public int Id { get; set; }
    
    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Difficulty value must be non-negative")]
    public int DifficultyValue { get; set; }
    
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}