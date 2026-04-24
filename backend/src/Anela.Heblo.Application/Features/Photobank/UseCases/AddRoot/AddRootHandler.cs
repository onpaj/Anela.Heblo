using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Photobank.Contracts;
using Anela.Heblo.Domain.Features.Photobank;
using MediatR;

namespace Anela.Heblo.Application.Features.Photobank.UseCases.AddRoot
{
    public class AddRootHandler : IRequestHandler<AddRootRequest, AddRootResponse>
    {
        private readonly IPhotobankRepository _repository;

        public AddRootHandler(IPhotobankRepository repository)
        {
            _repository = repository;
        }

        public async Task<AddRootResponse> Handle(AddRootRequest request, CancellationToken cancellationToken)
        {
            var root = new PhotobankIndexRoot
            {
                SharePointPath = request.SharePointPath.Trim(),
                DisplayName = request.DisplayName?.Trim(),
                DriveId = request.DriveId.Trim(),
                RootItemId = request.RootItemId.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            var created = await _repository.AddRootAsync(root, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);

            return new AddRootResponse { Id = created.Id };
        }
    }
}
