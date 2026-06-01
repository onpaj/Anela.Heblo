using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;

public class DownloadFromUrlRequest : IRequest<DownloadFromUrlResponse>
{
    [Required]
    public string FileUrl { get; set; } = null!;

    [Required]
    public string ContainerName { get; set; } = null!;

    public string? BlobName { get; set; }
}