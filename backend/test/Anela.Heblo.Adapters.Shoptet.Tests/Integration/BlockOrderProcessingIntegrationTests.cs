using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class BlockOrderProcessingIntegrationTests
{
    private const int StatusNova = -1;
    private const int StatusPoznamka = 35;
    private const int StatusVyrizujeSe = -2;
    private const int StatusBaliSe = 26;

    private const string TestEmail = "block-order-test@heblo.test";

    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;
    private readonly ITestOutputHelper _output;

    public BlockOrderProcessingIntegrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _logger = fixture.ServiceProvider.GetRequiredService<ILogger<BlockOrderProcessingHandler>>();
        _output = output;
    }

    [Fact]
    public async Task BlockOrder_Nova_Succeeds()
    {
        await RunTest("BLOCK-ORDER-TEST-NOVA", StatusNova, shouldSucceed: true);
    }

    [Fact]
    public async Task BlockOrder_Poznamka_Succeeds()
    {
        await RunTest("BLOCK-ORDER-TEST-POZNAMKA", StatusPoznamka, shouldSucceed: true);
    }

    [Fact]
    public async Task BlockOrder_VyrizujeSe_Succeeds()
    {
        await RunTest("BLOCK-ORDER-TEST-VYRIZI", StatusVyrizujeSe, shouldSucceed: true);
    }

    [Fact]
    public async Task BlockOrder_PreservesExistingEshopRemark_AndAppendsOnNewLine()
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_BLOCK_ORDER") != "1")
            return;

        AssertTestEnvironment(_configuration);

        var blockedStatusId = _configuration.GetValue<int?>("ShoptetOrders:BlockedStatusId")
            ?? throw new InvalidOperationException(
                "Missing ShoptetOrders:BlockedStatusId in configuration. "
                + "Add it to user secrets — use GET /api/eshop?include=orderStatuses to discover valid IDs.");

        var handler = new BlockOrderProcessingHandler(
            _client,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = [StatusNova, StatusPoznamka, StatusVyrizujeSe],
                BlockedStatusId = blockedStatusId,
            }),
            _logger);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]
            ?? throw new InvalidOperationException("Missing Shoptet:PaymentMethodGuid in configuration.");
        var shippingGuid = _configuration["Shoptet:ShippingGuidMap:21"]
            ?? throw new InvalidOperationException("Missing Shoptet:ShippingGuidMap:21 in configuration.");

        string? code = null;
        try
        {
            code = await _client.CreateOrderAsync(BuildOrderRequest("BLOCK-ORDER-TEST-APPEND", shippingGuid, paymentGuid), ct);
            await _client.UpdateStatusAsync(code, StatusNova, ct);
            await _client.UpdateEshopRemarkAsync(code, "pre-existing note from setup", ct);
            _output.WriteLine($"CREATED  BLOCK-ORDER-TEST-APPEND ({code}) → status {StatusNova} with pre-existing remark");

            var result = await handler.Handle(
                new BlockOrderProcessingRequest { OrderCode = code, Note = "block reason from test" },
                ct);

            result.Success.Should().BeTrue("order in state Nova with pre-existing remark should be blockable");

            var remarkAfter = await _client.GetEshopRemarkAsync(code, ct);
            remarkAfter.Should().Be("pre-existing note from setup\nblock reason from test");

            _output.WriteLine($"REMARK   BLOCK-ORDER-TEST-APPEND ({code}) ✓");
        }
        finally
        {
            if (code != null)
            {
                await _client.DeleteOrderAsync(code, ct);
                _output.WriteLine($"DELETED  {code}");
            }
        }
    }

    [Fact]
    public async Task BlockOrder_BaliSe_IsRejectedWithInvalidSourceStateError()
    {
        await RunTest("BLOCK-ORDER-TEST-BALI", StatusBaliSe, shouldSucceed: false);
    }

    private async Task RunTest(string externalCode, int statusId, bool shouldSucceed)
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_BLOCK_ORDER") != "1")
            return;

        ShoptetTestGuard.Assert(_configuration);

        var blockedStatusId = _configuration.GetValue<int?>("ShoptetOrders:BlockedStatusId")
            ?? throw new InvalidOperationException(
                "Missing ShoptetOrders:BlockedStatusId in configuration. "
                + "Add it to user secrets — use GET /api/eshop?include=orderStatuses to discover valid IDs.");

        var handler = new BlockOrderProcessingHandler(
            _client,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = [StatusNova, StatusPoznamka, StatusVyrizujeSe],
                BlockedStatusId = blockedStatusId,
            }),
            _logger);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]
            ?? throw new InvalidOperationException("Missing Shoptet:PaymentMethodGuid in configuration.");
        var shippingGuid = _configuration["Shoptet:ShippingGuidMap:21"]
            ?? throw new InvalidOperationException("Missing Shoptet:ShippingGuidMap:21 in configuration.");

        string? code = null;
        try
        {
            code = await _client.CreateOrderAsync(BuildOrderRequest(externalCode, shippingGuid, paymentGuid), ct);
            await _client.UpdateStatusAsync(code, statusId, ct);
            _output.WriteLine($"CREATED  {externalCode} ({code}) → status {statusId}");

            var result = await handler.Handle(
                new BlockOrderProcessingRequest { OrderCode = code, Note = "Integration test block" },
                ct);

            if (shouldSucceed)
            {
                result.Success.Should().BeTrue(
                    $"order in state {statusId} ({externalCode}) should be blockable");

                var remarkAfter = await _client.GetEshopRemarkAsync(code, ct);
                remarkAfter.Should().EndWith("Integration test block");

                _output.WriteLine($"BLOCKED  {externalCode} ({code}) ✓");
            }
            else
            {
                result.Success.Should().BeFalse(
                    $"order in state {statusId} ({externalCode}) should not be blockable");
                result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderInvalidSourceState);
                _output.WriteLine($"REJECTED {externalCode} ({code}) ✓");
            }
        }
        finally
        {
            if (code != null)
            {
                await _client.DeleteOrderAsync(code, ct);
                _output.WriteLine($"DELETED  {code}");
            }
        }
    }

    private static CreateEshopOrderRequest BuildOrderRequest(
        string externalCode,
        string shippingGuid,
        string paymentGuid) =>
        new()
        {
            Email = TestEmail,
            Phone = "+420725191660",
            ExternalCode = externalCode,
            ShippingGuid = shippingGuid,
            PaymentMethodGuid = paymentGuid,
            CurrencyCode = "CZK",
            BillingAddress = new EshopOrderAddress
            {
                FullName = "Test Heblo",
                Street = "Testovaci 1",
                City = "Praha",
                Zip = "10000",
            },
            Items =
            [
                new EshopOrderItem
                {
                    ItemType = "product",
                    Code = "OCH001030",
                    Name = "Test product",
                    VatRate = "21",
                    ItemPriceWithVat = "1.00",
                    Amount = "1",
                },
                new EshopOrderItem
                {
                    ItemType = "billing",
                    Name = "Platba prevod",
                    VatRate = "0",
                    ItemPriceWithVat = "0.00",
                    Amount = "1",
                },
                new EshopOrderItem
                {
                    ItemType = "shipping",
                    Name = "Zásilkovna (do ruky)",
                    VatRate = "0",
                    ItemPriceWithVat = "0.00",
                    Amount = "1",
                },
            ],
        };

}
