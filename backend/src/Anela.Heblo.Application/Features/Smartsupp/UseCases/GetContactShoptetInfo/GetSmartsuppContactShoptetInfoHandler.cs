using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoHandler
    : IRequestHandler<GetSmartsuppContactShoptetInfoRequest, GetSmartsuppContactShoptetInfoResponse>
{
    private readonly ISmartsuppRepository _repo;
    private readonly IShoptetCustomerClient _customerClient;

    public GetSmartsuppContactShoptetInfoHandler(
        ISmartsuppRepository repo,
        IShoptetCustomerClient customerClient)
    {
        _repo = repo;
        _customerClient = customerClient;
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

        ShoptetCustomerInfoDto? customer = null;

        if (!string.IsNullOrWhiteSpace(userGuid))
            customer = await _customerClient.GetCustomerByGuidAsync(userGuid, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(shoptetGuid))
            customer = await _customerClient.GetCustomerByGuidAsync(shoptetGuid, cancellationToken);

        if (customer is null)
            return new GetSmartsuppContactShoptetInfoResponse { ContactInfo = null };

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
                RecentOrders = new(),
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
