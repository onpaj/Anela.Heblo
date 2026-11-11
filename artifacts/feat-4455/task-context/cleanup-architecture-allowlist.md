### task: cleanup-architecture-allowlist
Remove the obsolete boundary exceptions. Depends on: switch-consumer.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] Delete the two entries (`... -> IManufactureClient` and `... -> ProductPart`) and their comments from `LogisticsAllowlist`; leave the set empty and update the lead comment to "Empty -- no active violations." (match the style of `PurchaseAllowlist`).
- [ ] Run `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ModuleBoundariesTests"` and expect pass. If a stale-allowlist test exists it should now pass too.
- [ ] Commit: `test(architecture): drop Logistics -> Manufacture allowlist entries`.

