using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.DeleteManufactureDifficulty;

public class DeleteManufactureDifficultyRequest : IRequest<DeleteManufactureDifficultyResponse>
{
    [Required]
    public int Id { get; set; }
}