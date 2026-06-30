### task: verify-build

- [ ] Full solution build succeeds
- [ ] Format check passes with no changes
- [ ] All tests pass

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

All three commands must succeed with no errors.
