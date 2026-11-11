# Impl controller-fix r1
Changed `EnableTile`/`DisableTile` in DashboardController to return `ActionResult<EnableTileResponse>`/`ActionResult<DisableTileResponse>`, capturing the mediator response and returning `HandleResponse(response)`. Other actions untouched.
Verification: dotnet build of Anela.Heblo.Tests 0 errors; `dotnet test --filter Dashboard` 193 passed, 0 failed; dotnet format --verify-no-changes clean for the file.
