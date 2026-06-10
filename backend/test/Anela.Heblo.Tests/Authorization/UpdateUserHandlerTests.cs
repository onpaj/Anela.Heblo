using Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class UpdateUserHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"updateuser_{Guid.NewGuid()}").Options);

    private static IPermissionResolver NoOpResolver() => new Mock<IPermissionResolver>().Object;

    private static async Task<AppUser> SeedUser(ApplicationDbContext db)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = "oid",
            Email = "old@x.cz",
            DisplayName = "Old",
            IsActive = true,
            CanPack = false,
            Source = AppUserSource.Entra,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_UpdatesFields_WhenUserExists()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = user.Id,
            DisplayName = "  New Name  ",
            Email = "  new@x.cz  ",
            CanPack = true,
        }, default);

        result.Success.Should().BeTrue();
        var saved = await db.AppUsers.SingleAsync(u => u.Id == user.Id);
        saved.DisplayName.Should().Be("New Name");   // trimmed
        saved.Email.Should().Be("new@x.cz");          // trimmed
        saved.CanPack.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllowsEmptyEmail()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = user.Id,
            DisplayName = "Name",
            Email = null,
            CanPack = false,
        }, default);

        result.Success.Should().BeTrue();
        (await db.AppUsers.SingleAsync(u => u.Id == user.Id)).Email.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserMissing()
    {
        await using var db = NewDb();
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), NoOpResolver());

        var result = await handler.Handle(new UpdateUserRequest
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Name",
        }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthorizationUserNotFound);
    }

    [Fact]
    public async Task Handle_InvalidatesCache_WhenEntraUser()
    {
        await using var db = NewDb();
        var user = await SeedUser(db);
        var resolverMock = new Mock<IPermissionResolver>();
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), resolverMock.Object);

        await handler.Handle(new UpdateUserRequest { UserId = user.Id, DisplayName = "Name", CanPack = true }, default);

        resolverMock.Verify(r => r.InvalidateCache(user.EntraObjectId!), Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotInvalidateCache_WhenLocalUser()
    {
        await using var db = NewDb();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            EntraObjectId = null,          // Local operator
            Email = "op@x.cz",
            DisplayName = "Operator",
            IsActive = true,
            CanPack = false,
            Source = AppUserSource.Local,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var resolverMock = new Mock<IPermissionResolver>();
        var handler = new UpdateUserHandler(new AuthorizationRepository(db), resolverMock.Object);

        await handler.Handle(new UpdateUserRequest { UserId = user.Id, DisplayName = "Op" }, default);

        resolverMock.Verify(r => r.InvalidateCache(It.IsAny<string>()), Times.Never);
    }
}
