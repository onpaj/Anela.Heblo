### task: final-validation

Confirm the entire solution builds, passes format check, and all Photobank tests are green.

**Steps:**

1. Full solution build:

```
dotnet build backend/Anela.Heblo.sln
```

Expected: zero errors.

2. Format check (CI gate):

```
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

If it reports changes, run `dotnet format backend/Anela.Heblo.sln` to apply them, then re-run `--verify-no-changes`.

3. Photobank tests:

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Photobank"
```

Expected: all tests pass, zero skipped.

4. Commit with a descriptive message covering all changed files.
