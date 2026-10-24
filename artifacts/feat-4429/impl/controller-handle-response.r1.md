# controller-handle-response r1
- Replaced the 5 inline `if (!response.Success) return BadRequest(response); return Ok(response);` blocks (CreateNewTransportBox, AddItemToBox, RemoveItemFromBox, UpdateTransportBoxDescription, GetTransportBoxByCode) with `return HandleResponse(response);`.
- GetTransportBoxById: handler returns Success=true with TransportBox=null on not-found (no ErrorCode), so HandleResponse cannot yield 404; kept the null check and added an explanatory comment.
- BaseApiController, routes, signatures, attributes untouched.
- Verification: `dotnet build src/Anela.Heblo.API` 0 errors; `grep BadRequest(response)` in controller returns nothing.
- Note: failures carrying an ErrorCode now map per HttpStatusCodeAttribute (default BadRequest) - intended by the issue.
