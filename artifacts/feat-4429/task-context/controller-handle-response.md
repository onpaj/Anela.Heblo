### task: controller-handle-response
Edit `TransportBoxController.cs`. In `CreateNewTransportBox`, `AddItemToBox`, `RemoveItemFromBox`, `UpdateTransportBoxDescription`, `GetTransportBoxByCode`, replace
`if (!response.Success) { return BadRequest(response); } return Ok(response);` with `return HandleResponse(response);`. Then read `GetTransportBoxByIdHandler`: if not-found is signalled with `Success=false` + `ErrorCodes.TransportBoxNotFound`, use `HandleResponse` there too; otherwise keep the `TransportBox == null` check and add a one-line comment explaining it. Do not change `BaseApiController`, routes, signatures or attributes.
Check: `cd backend && dotnet build` succeeds; `grep -n "BadRequest(response)" src/Anela.Heblo.API/Controllers/TransportBoxController.cs` returns nothing.

