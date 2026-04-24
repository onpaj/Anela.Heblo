using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.GetPhotos
{
    public class GetPhotosHandler : IRequestHandler<GetPhotosRequest, GetPhotosResponse>
    {
        private readonly IPhotobankRepository _repository;

        public GetPhotosHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetPhotosResponse> Handle(GetPhotosRequest request, CancellationToken cancellationToken)
        {
            var (items, total) = await _repository.GetPhotosAsync(
                request.Tags,
                request.Search,
                request.Page,
                request.PageSize,
                cancellationToken);

            return new GetPhotosResponse
            {
                Items = items.Select(MapToDto).ToList(),
                Total = total,
                Page = request.Page,
                PageSize = request.PageSize,
            };
        }

        internal static PhotoDto MapToDto(Photo photo) => new()
        {
            Id = photo.Id,
            SharePointFileId = photo.SharePointFileId,
            Name = photo.FileName,
            FolderPath = photo.FolderPath,
            SharePointWebUrl = photo.SharePointWebUrl,
            FileSizeBytes = photo.FileSizeBytes,
            LastModifiedAt = photo.ModifiedAt,
            Tags = photo.Tags.Select(pt => new TagDto
            {
                Id = pt.TagId,
                Name = pt.Tag.Name,
                Source = pt.Source.ToString(),
            }).ToList(),
        };
    }
}
