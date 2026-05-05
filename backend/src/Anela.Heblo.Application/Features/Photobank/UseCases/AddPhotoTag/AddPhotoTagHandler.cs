using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddPhotoTag
{
    public class AddPhotoTagHandler : IRequestHandler<AddPhotoTagRequest, AddPhotoTagResponse>
    {
        private readonly IPhotobankRepository _repository;

        public AddPhotoTagHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<AddPhotoTagResponse> Handle(AddPhotoTagRequest request, CancellationToken cancellationToken)
        {
            var photo = await _repository.GetPhotoByIdAsync(request.PhotoId, cancellationToken);
            if (photo == null)
                return new AddPhotoTagResponse(ErrorCodes.PhotoNotFound);

            var normalizedName = request.TagName.Trim().ToLowerInvariant();
            var tag = await _repository.GetOrCreateTagAsync(normalizedName, cancellationToken);

            if (await _repository.PhotoTagExistsAsync(photo.Id, tag!.Id, cancellationToken))
                return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };

            var photoTag = new PhotoTag
            {
                PhotoId = photo.Id,
                TagId = tag.Id,
                Source = PhotoTagSource.Manual,
                CreatedAt = DateTime.UtcNow,
            };

            await _repository.AddPhotoTagAsync(photoTag, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new AddPhotoTagResponse { TagId = tag.Id, TagName = tag.Name };
        }
    }
}
