---
process: module-shoptet-customers
kind: module
module: shoptet-customers
summary: Read-only lookup of a Shoptet customer account by GUID, used to show who a Smartsupp chat visitor is (customer group, price list, address).
owns: []
verified_at: "5e993f9e2"
related: []
---

# Shoptet customers

## Purpose
When a logged-in e-shop visitor opens a Smartsupp chat, customer support wants to know who they
are: wholesale or retail customer group, which price list they buy at, and their address. This
module fetches that customer record live from Shoptet. It is a thin contract
(`IShoptetCustomerClient`) with one read call; it stores nothing and changes nothing.

## Users & screens
- Customer support on `/customer/smartsupp` — the Shoptet customer card (`ShoptetCustomerCard`)
  in the chat detail panel.
- Reached through the Smartsupp API `GET /api/smartsupp/conversations/{id}/shoptet-info`
  (permission `Customer_Smartsupp`); this module has no controller or MCP tool of its own.

## Processes
No process docs: the module only performs an on-demand read. Flow (in the Smartsupp handler
`GetSmartsuppContactShoptetInfoHandler`): read the conversation's stored Smartsupp variables →
take `shoptet_user_guid`, else `shoptet_guid` → `GET /api/customers/{guid}` → map to
`ShoptetCustomerInfoDto` → return. No GUID or a 404 ⇒ no customer card. `shoptet_cart_updated_at`
is passed through as the cart timestamp. Smartsupp itself is documented by the smartsupp module.

## Data owned
None.

## External systems
Shoptet REST API, read only: `GET /api/customers/{guid}` (`ShoptetCustomerClient`, header
`Shoptet-Private-API-Token`). 404 → null; other non-2xx → `HttpRequestException`.

Mapping: `CustomerGroup` ← `customerGroup.name`, `PriceList` ← `priceList.name`,
`DefaultShippingAddress` ← `billingAddress` `countryCode, city, zip, street` joined with ", ",
`FullName` ← top-level `fullName`, `Email` ← top-level `email`.

## Dependencies
- Reads from: nothing in Heblo.
- Read by: Smartsupp (`GetSmartsuppContactShoptetInfoHandler`) — the only consumer.

## Known quirks
- **Email and name are probably always empty.** The client reads top-level `email` and
  `fullName`, but the verified response shape (`docs/integrations/shoptet-api.md` §3.15,
  2026-09-22) has no top-level `email` (it is on `accounts[]`) and the name lives on
  `billingAddress.fullName` / `accounts[].fullName`. Only group, price list and address are
  reliably filled.
- "Default shipping address" is actually the **billing** address and omits the house number
  (`billingAddress.houseNumber` is not read); the real delivery addresses are the
  `deliveryAddress[]` array, which is ignored.
- Recent orders on the card are always empty: `RecentOrders = new()` with a TODO — the e-mail
  based order lookup was removed and no GUID-based one exists.
- Guest checkout dominates (69% of orders have no `customerGuid`, `shoptet-api.md` §3.16,
  2026-09-22), so most chat visitors cannot be resolved to a customer at all.
- Live call on every card load, no caching, no retry.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/IShoptetCustomerClient.cs` — contract
- `backend/src/Anela.Heblo.Application/Features/ShoptetCustomers/ShoptetCustomerInfoDto.cs` — returned fields
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Customers/ShoptetCustomerClient.cs` — HTTP call and mapping
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs` — the consumer
