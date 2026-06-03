using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Asserts that the three Catalog-owned source contracts are resolvable from the
/// application's IServiceProvider. Each adapter is registered by its provider module;
/// this test fails if any of those bindings are removed.
/// </summary>
public class CatalogModuleContractResolutionTests : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;

    public CatalogModuleContractResolutionTests(ManufactureOrderTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TransportSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogTransportSource>();
        resolved.Should().NotBeNull();
    }

    [Fact]
    public void PurchaseSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogPurchaseSource>();
        resolved.Should().NotBeNull();
    }

    [Fact]
    public void ManufactureSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogManufactureSource>();
        resolved.Should().NotBeNull();
    }
}
