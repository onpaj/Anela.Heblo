using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogMergeCallbackWiringTests
{
    [Fact]
    public async Task HostStart_WiresCallbackAndPriorityMergeReturnsErpSeededList()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                services.AddMemoryCache();
                services.AddSingleton(TimeProvider.System);
                services.Configure<CatalogCacheOptions>(o =>
                {
                    o.EnableBackgroundMerge = true;
                    o.AllowStaleDataDuringMerge = false;
                });
                services.Configure<DataSourceOptions>(o => { });

                services.AddSingleton<ICatalogResilienceService, CatalogResilienceService>();
                services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();
                services.AddSingleton<CatalogCacheStore>();
                services.AddSingleton<CatalogMergeService>();
                services.AddTransient<CatalogDataRefreshService>();
                services.AddTransient<ICatalogRepository, CatalogRepository>();
                services.AddHostedService<CatalogMergeCallbackWiring>();

                services.AddSingleton(Mock.Of<ICatalogSalesClient>());
                services.AddSingleton(Mock.Of<ICatalogAttributesClient>());
                services.AddSingleton(Mock.Of<IEshopStockClient>());
                services.AddSingleton(Mock.Of<IConsumedMaterialsClient>());
                services.AddSingleton(Mock.Of<IPurchaseHistoryClient>());
                services.AddSingleton(Mock.Of<ILotsClient>());
                services.AddSingleton(Mock.Of<IProductPriceEshopClient>());
                services.AddSingleton(Mock.Of<IProductPriceErpClient>());
                services.AddSingleton(Mock.Of<IProductEshopUrlClient>());
                services.AddSingleton(Mock.Of<ITransportBoxRepository>());
                services.AddSingleton(Mock.Of<IStockTakingRepository>());
                services.AddSingleton(Mock.Of<IPurchaseOrderRepository>());
                services.AddSingleton(Mock.Of<IManufactureOrderRepository>());
                services.AddSingleton(Mock.Of<IManufactureHistoryClient>());
                services.AddSingleton(Mock.Of<IManufactureDifficultyRepository>());
                services.AddSingleton(Mock.Of<IManufacturedProductInventoryRepository>());

                var erpStockMock = new Mock<IErpStockClient>();
                erpStockMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ErpStock>
                    {
                        new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1 },
                        new() { ProductCode = "P2", ProductName = "Product 2", ProductId = 2 },
                    });
                services.AddSingleton(erpStockMock.Object);
            });

        using var host = hostBuilder.Build();
        await host.StartAsync();

        try
        {
            var repo = host.Services.GetRequiredService<ICatalogRepository>();

            await repo.RefreshErpStockData(CancellationToken.None);

            var all = (await repo.GetAllAsync(CancellationToken.None)).ToList();

            all.Should().HaveCount(2);
            all.Should().Contain(p => p.ProductCode == "P1");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
