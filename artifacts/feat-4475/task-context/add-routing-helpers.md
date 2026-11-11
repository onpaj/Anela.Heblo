### task: add-routing-helpers
**Files:** modify `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetOrderClientTests.cs`

- [ ] Add private helpers `BuildOrderRoutingClient`, `ListPage`, `Detail` as described in design.r1.md (record requested `PathAndQuery`; route list vs detail; 404 otherwise).
- [ ] Run `dotnet build backend/test/Anela.Heblo.Tests` and confirm it compiles.
- [ ] Commit: `test(shoptet): add routing helpers for order client tests`

