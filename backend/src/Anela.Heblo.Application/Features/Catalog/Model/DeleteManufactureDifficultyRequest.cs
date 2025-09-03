using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class DeleteManufactureDifficultyRequest : IRequest<DeleteManufactureDifficultyResponse>
{
    [Required]
    public int Id { get; set; }
}