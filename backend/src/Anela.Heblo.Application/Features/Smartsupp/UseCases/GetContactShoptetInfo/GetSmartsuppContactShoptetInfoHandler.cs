using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoHandler
    : IRequestHandler<GetSmartsuppContactShoptetInfoRequest, GetSmartsuppContactShoptetInfoResponse>
{
    private const int RecentOrdersLimit = 5;

    private readonly ISmartsuppRepository _repo;
    private readonly IShoptetCustomerClient _customerClient;
    private readonly IEshopOrderClient _orderClient;

    public GetSmartsuppContactShoptetInfoHandler(
        ISmartsuppRepository repo,
        IShoptetCustomerClient customerClient,
        IEshopOrderClient orderClient)
    {
        _repo = repo;
        _customerClient = customerClient;
        _orderClient = orderClient;
    }

    public async Task<GetSmartsuppContactShoptetInfoResponse> Handle(
        GetSmartsuppContactShoptetInfoRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repo.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GetSmartsuppContactShoptetInfoResponse(ErrorCodes.SmartsuppConversationNotFound);

        var variables = ParseVariables(conversation.VariablesJson);
        variables.TryGetValue("shoptet_user_guid", out var userGuid);
        variables.TryGetValue("shoptet_guid", out var shoptetGuid);
        variables.TryGetValue("shoptet_cart_updated_at", out var cartStr);

        // Resolution order: shoptet_user_guid → shoptet_guid → email (first match wins)
        ShoptetCustomerInfoDto? customer = null;
        List<EshopOrderInfo>? preloadedOrders = null;

        if (!string.IsNullOrWhiteSpace(userGuid))
        {
            customer = await _customerClient.GetCustomerByGuidAsync(userGuid, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(shoptetGuid))
        {
            customer = await _customerClient.GetCustomerByGuidAsync(shoptetGuid, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(conversation.ContactEmail))
        {
            preloadedOrders = await _orderClient.GetRecentOrdersByEmailAsync(
                conversation.ContactEmail, RecentOrdersLimit, cancellationToken);
            var firstGuid = preloadedOrders.FirstOrDefault()?.CustomerGuid;
            if (!string.IsNullOrWhiteSpace(firstGuid))
                customer = await _customerClient.GetCustomerByGuidAsync(firstGuid, cancellationToken);
        }

        if (customer is null)
            return new GetSmartsuppContactShoptetInfoResponse(ErrorCodes.SmartsuppShoptetCustomerNotFound);

        var lookupEmail = customer.Email ?? conversation.ContactEmail ?? string.Empty;
        var orders = preloadedOrders ?? await _orderClient.GetRecentOrdersByEmailAsync(lookupEmail, RecentOrdersLimit, cancellationToken);
        var statusNames = await _orderClient.GetOrderStatusNamesAsync(cancellationToken);

        DateTime? cartUpdatedAt = null;
        if (!string.IsNullOrWhiteSpace(cartStr) &&
            DateTime.TryParse(cartStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedCart))
            cartUpdatedAt = parsedCart;

        return new GetSmartsuppContactShoptetInfoResponse
        {
            ContactInfo = new ShoptetContactInfoDto
            {
                Customer = new ShoptetCustomerSnapshotDto
                {
                    FullName = customer.FullName,
                    Email = customer.Email,
                    CustomerGroup = customer.CustomerGroup,
                    PriceList = customer.PriceList,
                    DefaultShippingAddress = customer.DefaultShippingAddress,
                },
                RecentOrders = orders.Select(o => new ShoptetOrderSnapshotDto
                {
                    Code = o.Code,
                    StatusName = statusNames.TryGetValue(o.StatusId, out var name) ? name : o.StatusId.ToString(),
                    TotalWithVat = o.TotalWithVat,
                    CurrencyCode = o.CurrencyCode,
                    OrderDate = o.OrderDate,
                    AdminUrl = o.AdminUrl,
                }).ToList(),
                CartUpdatedAt = cartUpdatedAt,
            },
        };
    }

    private static Dictionary<string, string> ParseVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
