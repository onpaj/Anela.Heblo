using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Manual")]
[Trait("Category", "Integration")]
public class CaptureInvoice126000039Fixture
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CaptureInvoice126000039Fixture(ShoptetIntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact(Skip = "Manual: remove Skip locally to capture fixture.")]
    public async Task Capture()
    {
        var client = _fixture.ServiceProvider.GetRequiredService<IShoptetInvoiceClient>();
        var raw = await client.GetInvoiceRawJsonAsync("126000039", default);
        raw.Should().NotBeNullOrWhiteSpace();

        // Save to source tree (not bin/), 4 levels up from bin/Debug/net8.0/
        var fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "IssuedInvoices", "Fixtures", "invoice-126000039.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        await File.WriteAllTextAsync(fixturePath, raw);

        // Parse and log discount shape
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var data = root.GetProperty("data");
        var invoice = data.GetProperty("invoice");

        _output.WriteLine($"Invoice totals:");
        _output.WriteLine($"  withVat={invoice.GetProperty("price").GetProperty("withVat")}");
        if (invoice.GetProperty("price").TryGetProperty("toPay", out var toPay))
            _output.WriteLine($"  toPay={toPay}");

        _output.WriteLine("Items:");
        foreach (var item in invoice.GetProperty("items").EnumerateArray())
        {
            var itemType = item.TryGetProperty("itemType", out var it) ? it.GetString() : "null";
            var code = item.TryGetProperty("code", out var c) ? c.GetString() : "null";
            var amount = item.TryGetProperty("amount", out var a) ? a.GetString() : "null";
            var priceRatio = item.TryGetProperty("priceRatio", out var pr) ? pr.ToString() : "null";
            var unitWithVat = item.TryGetProperty("unitPrice", out var up) && up.TryGetProperty("withVat", out var uwv) ? uwv.GetString() : "null";
            var itemWithVat = item.TryGetProperty("itemPrice", out var ip) && ip.TryGetProperty("withVat", out var iwv) ? iwv.GetString() : "null";
            _output.WriteLine($"  itemType={itemType} code={code} amount={amount} priceRatio={priceRatio} unitWithVat={unitWithVat} itemWithVat={itemWithVat}");
        }
    }
}
