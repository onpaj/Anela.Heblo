using Anela.Heblo.Application.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class AuthorizationModuleTests
{
    [Fact]
    public void AddAuthorizationModule_RegistersResolverAndRepository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase("authz_module"));
        services.AddAuthorizationModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IPermissionResolver>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IAuthorizationRepository>().Should().NotBeNull();
    }
}
