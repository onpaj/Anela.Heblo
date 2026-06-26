using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserHandler : IRequestHandler<CreateLocalUserRequest, CreateLocalUserResponse>
{
    private readonly IAuthorizationRepository _repo;

    public CreateLocalUserHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<CreateLocalUserResponse> Handle(CreateLocalUserRequest request, CancellationToken ct)
    {
        var name = request.DisplayName.Trim();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = null,
            Email = string.Empty,
            DisplayName = name,
            IsActive = true,
            Source = AppUserSource.Local,
            CanPack = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _repo.AddUserAsync(user, ct);
        await _repo.SaveChangesAsync(ct);

        return new CreateLocalUserResponse
        {
            User = new AppUserDto
            {
                Id = user.Id,
                EntraObjectId = null,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                Source = user.Source.ToString(),
                CanPack = user.CanPack,
                GroupIds = new List<Guid>(),
            },
        };
    }
}
