using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetSmartsuppContactShoptetInfoHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<IShoptetCustomerClient> _customerClient = new();
    private readonly Mock<IEshopOrderClient> _orderClient = new();

    private GetSmartsuppContactShoptetInfoHandler CreateHandler() =>
        new(_repo.Object, _customerClient.Object, _orderClient.Object);

    private static ShoptetCustomerInfoDto MakeCustomer(string guid = "cust-1") =>
        new() { Guid = guid, FullName = "Jana Nováková", Email = "jana@test.cz", CustomerGroup = "VIP", PriceList = "Retail" };

    private static List<EshopOrderInfo> MakeOrders(string customerGuid = "cust-1") =>
    [
        new() { Code = "2024001", CustomerGuid = customerGuid, TotalWithVat = 1250m, CurrencyCode = "CZK", StatusId = 26, AdminUrl = "https://anela.myshoptet.com/admin/orders/2024001", OrderDate = new DateTime(2026, 4, 1) },
    ];

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((SmartsuppConversation?)null);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ResolvesViaUserGuid_WhenShoptetUserGuidPresent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_user_guid":"user-guid-1","shoptet_guid":"guid-2"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("user-guid-1", It.IsAny<CancellationToken>())).ReturnsAsync(MakeCustomer("user-guid-1"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, It.IsAny<CancellationToken>())).ReturnsAsync(MakeOrders("user-guid-1"));
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<int, string> { { 26, "Balí se" } });

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        // Must use user-guid-1, NOT guid-2
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("user-guid-1", It.IsAny<CancellationToken>()), Times.Once);
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-2", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ResolvesViaShoptetGuid_WhenUserGuidAbsent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-abc"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>())).ReturnsAsync(MakeCustomer("guid-abc"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, It.IsAny<CancellationToken>())).ReturnsAsync(MakeOrders("guid-abc"));
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<int, string>());

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ResolvesViaEmail_WhenNoGuidsPresent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "jana@test.cz",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, It.IsAny<CancellationToken>())).ReturnsAsync(MakeOrders("cust-1"));
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("cust-1", It.IsAny<CancellationToken>())).ReturnsAsync(MakeCustomer("cust-1"));
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<int, string> { { 26, "Balí se" } });

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.Customer.FullName.Should().Be("Jana Nováková");
        result.ContactInfo.RecentOrders.Should().HaveCount(1);
        result.ContactInfo.RecentOrders[0].StatusName.Should().Be("Balí se");
        result.ContactInfo.RecentOrders[0].TotalWithVat.Should().Be(1250m);
    }

    [Fact]
    public async Task Handle_ReturnsCustomerNotFound_WhenNoGuidAndNoMatchingOrders()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "unknown@test.cz",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("unknown@test.cz", 5, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppShoptetCustomerNotFound);
    }

    [Fact]
    public async Task Handle_ParsesCartUpdatedAt_FromVariables()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-1","shoptet_cart_updated_at":"2026-04-15T12:00:00"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-1", It.IsAny<CancellationToken>())).ReturnsAsync(MakeCustomer("guid-1"));
        _orderClient.Setup(o => o.GetRecentOrdersByEmailAsync("jana@test.cz", 5, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _orderClient.Setup(o => o.GetOrderStatusNamesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<int, string>());

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.CartUpdatedAt.Should().Be(new DateTime(2026, 4, 15, 12, 0, 0));
    }
}
