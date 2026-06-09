using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationRepositoryPackingUsersTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task GetActivePackingUsersAsync_ReturnsOnlyActiveCanPackUsers_OrderedByName()
    {
        await using var db = NewDb();
        db.AppUsers.AddRange(
            new AppUser { Id = Guid.NewGuid(), Email = "z@x.cz", DisplayName = "Zoe", IsActive = true, CanPack = true, Source = AppUserSource.Local, CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "a@x.cz", DisplayName = "Ada", IsActive = true, CanPack = true, Source = AppUserSource.Entra, EntraObjectId = "oid-a", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "n@x.cz", DisplayName = "NonPacker", IsActive = true, CanPack = false, Source = AppUserSource.Entra, EntraObjectId = "oid-n", CreatedAt = DateTimeOffset.UtcNow },
            new AppUser { Id = Guid.NewGuid(), Email = "i@x.cz", DisplayName = "Inactive", IsActive = false, CanPack = true, Source = AppUserSource.Local, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var repo = new AuthorizationRepository(db);
        var result = await repo.GetActivePackingUsersAsync();

        result.Select(u => u.DisplayName).Should().Equal("Ada", "Zoe");
    }
}
