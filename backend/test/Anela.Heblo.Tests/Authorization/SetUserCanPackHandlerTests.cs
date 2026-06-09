using Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class SetUserCanPackHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_SetsCanPack_WhenUserExists()
    {
        await using var db = NewDb();
        var id = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = id, Email = "u@x.cz", DisplayName = "U", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var handler = new SetUserCanPackHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new SetUserCanPackRequest { UserId = id, CanPack = true }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.FindAsync(id))!.CanPack.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        await using var db = NewDb();
        var handler = new SetUserCanPackHandler(new AuthorizationRepository(db));
        var result = await handler.Handle(new SetUserCanPackRequest { UserId = Guid.NewGuid(), CanPack = true }, default);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }
}
