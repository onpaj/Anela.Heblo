using Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class CreateLocalUserHandlerTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Handle_CreatesLocalPacker_WithNullEntraId()
    {
        await using var db = NewDb();
        var handler = new CreateLocalUserHandler(new AuthorizationRepository(db));

        var result = await handler.Handle(new CreateLocalUserRequest { DisplayName = "  Pepa  " }, default);

        result.Success.Should().BeTrue();
        result.User!.Source.Should().Be(nameof(AppUserSource.Local));
        result.User.CanPack.Should().BeTrue();
        var saved = await db.AppUsers.SingleAsync();
        saved.DisplayName.Should().Be("Pepa");          // trimmed
        saved.EntraObjectId.Should().BeNull();
        saved.IsActive.Should().BeTrue();
        saved.Source.Should().Be(AppUserSource.Local);
    }
}
