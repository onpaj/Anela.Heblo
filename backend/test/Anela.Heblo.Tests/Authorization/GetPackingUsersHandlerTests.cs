using Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GetPackingUsersHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_ReturnsActivePackersOnly()
    {
        await using var db = NewDb();
        db.AppUsers.AddRange(
            new AppUser { Id = Guid.NewGuid(), Email = "a@x.cz", DisplayName = "Ada", IsActive = true, CanPack = true, Source = AppUserSource.Entra, EntraObjectId = "oid-a", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "n@x.cz", DisplayName = "No", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid-n", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new GetPackingUsersHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new GetPackingUsersRequest(), default);

        result.Users.Should().ContainSingle();
        result.Users[0].DisplayName.Should().Be("Ada");
    }
}
