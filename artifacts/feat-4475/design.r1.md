# Design: ListByExternalCodePrefixAsync unit tests

## Component Design
- Private helper `BuildOrderRoutingClient(Dictionary<int, object> listPages, Dictionary<string, object> details, List<string> requests)` in `ShoptetOrderClientTests`: records `PathAndQuery`; if path matches `/api/orders?page=N...` return `listPages[N]`; if path matches `/api/orders/{code}` return `details[code]`; else 404.
- Private helpers `ListPage(int page, int pageCount, params (string code, string? email)[] orders)` and `Detail(code, externalCode, email, statusId)` build anonymous objects for `Json(...)`.
- Tests (names): `ListByExternalCodePrefixAsync_SinglePage_ReturnsOnlyPrefixMatches`, `_MultiplePages_FindsMatchOnLaterPageAndStops`, `_EmailFilter_ReturnsOnlyMatchingEmailCaseInsensitive` (asserts no detail call for filtered order), `_PageCountOne_NoMatch_ReturnsEmpty`, `_PageCountZero_ReturnsEmpty`, `_PrefixIsCaseSensitive_AndNullExternalCodeIgnored`.

## Data Schemas
List: `{ data: { orders: [{code,email,status:{id}}], paginator: {page,pageCount,totalCount} } }`
Detail: `{ data: { order: { code, externalCode, email, status:{id} } } }`
