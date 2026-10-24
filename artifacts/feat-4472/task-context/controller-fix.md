### task: controller-fix
**Files:** Modify `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`

- [ ] Change `EnableTile` to `public async Task<ActionResult<EnableTileResponse>>`, capture `var response = await _mediator.Send(request);` and `return HandleResponse(response);`.
- [ ] Same for `DisableTile` with `DisableTileResponse`.
- [ ] Leave all other actions untouched.
- [ ] `dotnet build` and `dotnet format` on the solution; run `dotnet test --filter "DashboardController"` (including `DashboardControllerAuthorizationTests`) and all tests under `Features/Dashboard`.

