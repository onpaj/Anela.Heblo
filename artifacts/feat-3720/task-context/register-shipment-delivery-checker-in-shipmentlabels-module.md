### task: register-shipment-delivery-checker-in-shipmentlabels-module

Wire `IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` into DI, inside `ShipmentLabelsModule` (the provider), following the same comment convention as `CarrierCoolingModule.cs`.

**Step 1 — write a failing DI-wiring test first.**

Create `backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs` (new file; folder already exists — it holds `GetOrderShipmentLabelsHandlerTests.cs` etc.):

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class ShipmentLabelsModuleTests
{
    [Fact]
    public void AddShipmentLabelsModule_RegistersIShipmentDeliveryChecker_AsShipmentLabelsShipmentDeliveryCheckerAdapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IShipmentClient>());
        var configuration = new ConfigurationBuilder().Build();

        services.AddShipmentLabelsModule(configuration);
        var serviceProvider = services.BuildServiceProvider();

        var checker = serviceProvider.GetRequiredService<IShipmentDeliveryChecker>();
        checker.Should().BeOfType<ShipmentLabelsShipmentDeliveryCheckerAdapter>();
    }
}
```

Run it and confirm it fails (either a compile error if the previous task wasn't merged, or — since the previous task is already done — an `InvalidOperationException: Unable to resolve service for type 'IShipmentDeliveryChecker'` at `GetRequiredService`, because the registration doesn't exist yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsModuleTests"
```

**Step 2 — add the DI registration.**

Read the current file first: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`. Add two `using` statements and the registration line.

Change the top of the file from:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```
to:
```csharp
using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Packaging.UseCases.GetPackageLabelPdf;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.GetOrderShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Validators;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Change the body from:
```csharp
        // Named HttpClient used by GetPackageLabelPdfHandler to stream carrier-CDN PDFs
        // through our own origin so the SPA can silent-print without CORS errors.
        services.AddHttpClient(GetPackageLabelPdfHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
```
to:
```csharp
        // Named HttpClient used by GetPackageLabelPdfHandler to stream carrier-CDN PDFs
        // through our own origin so the SPA can silent-print without CORS errors.
        services.AddHttpClient(GetPackageLabelPdfHandler.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Cross-module contract: ShipmentLabels implements ShoptetOrders' IShipmentDeliveryChecker via
        // adapter. DI registration is owned by the provider (ShipmentLabels), not the consumer (ShoptetOrders).
        services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();

        return services;
```

**Step 3 — run the test again and confirm it passes.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsModuleTests"
```

**Step 4 — build the whole solution.**

```bash
dotnet build Anela.Heblo.sln
```

**Step 5 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs \
        backend/test/Anela.Heblo.Tests/Application/ShipmentLabels/ShipmentLabelsModuleTests.cs
git commit -m "Register IShipmentDeliveryChecker in ShipmentLabelsModule"
```

---

