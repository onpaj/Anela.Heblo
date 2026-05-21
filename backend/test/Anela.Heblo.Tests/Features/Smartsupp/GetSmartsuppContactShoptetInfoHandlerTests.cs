using System.Globalization;
using Anela.Heblo.Application.Features.ShoptetCustomers;
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

    private GetSmartsuppContactShoptetInfoHandler CreateHandler() =>
        new(_repo.Object, _customerClient.Object);

    private static ShoptetCustomerInfoDto MakeCustomer(string guid = "cust-1") =>
        new() { Guid = guid, FullName = "Jana Nováková", Email = "jana@test.cz", CustomerGroup = "VIP", PriceList = "Retail" };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("missing", It.IsAny<CancellationToken>()))
             .ReturnsAsync((SmartsuppConversation?)null);

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
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("user-guid-1", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("user-guid-1"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.Customer!.FullName.Should().Be("Jana Nováková");
        result.ContactInfo.RecentOrders.Should().BeEmpty();
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
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("guid-abc"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsSuccessWithNullContactInfo()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"unknown-guid"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("unknown-guid", It.IsAny<CancellationToken>()))
                       .ReturnsAsync((ShoptetCustomerInfoDto?)null);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoGuidsPresent_ReturnsSuccessWithNullContactInfo()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "guest@example.com",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo.Should().BeNull();
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
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-1", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("guid-1"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.CartUpdatedAt.Should().Be(new DateTime(2026, 4, 15, 12, 0, 0));
        result.ContactInfo.RecentOrders.Should().BeEmpty();
    }
}
