### task: cover-new-to-opened-code-occupancy

**Files:**
- Modify (test): `backend/test/Anela.Heblo.Tests/Domain/Logistics/TransportBoxUniquenessTests.cs`
- No production file changes.

**Depends on:** `consume-rule-in-transport-box-repository`.

#### Goal

Pin the end-to-end consequence of the repository fix on the `New → Opened` path (FR-3) — the path the admin UI drives and the one that carries the reported bug. `ChangeTransportBoxStateHandler.HandleNewToOpened` needs **no code change**; it already calls `IsBoxCodeActiveAsync` and returns `TransportBoxDuplicateActiveBoxFound`. This task is tests only.

#### Context

- `TransportBoxUniquenessTests` wires the **real** `TransportBoxRepository` over an in-memory `ApplicationDbContext` into the **real** `ChangeTransportBoxStateHandler` (constructor, lines 31-67) — the right level for FR-3. The database name is inlined as `Guid.NewGuid().ToString()` at line 34.
- `HandleNewToOpened` (`ChangeTransportBoxStateHandler.cs:214-247`): checks `string.IsNullOrEmpty(request.BoxCode)`, normalises with `ToUpper()`, calls `IsBoxCodeActiveAsync`, and on `true` returns `Success = false` / `ErrorCode = TransportBoxDuplicateActiveBoxFound` / `Params { { "code", normalizedCode } }`. On `false` it closes same-code `Stocked` boxes and continues. Leave all of it alone.
- **Amendment A3 is binding.** Do **not** assert *"the `New` box's persisted `Code` remains `null`"*. `box.AssignBoxCodeIfAny(code)` mutates the tracked entity at `ChangeTransportBoxStateHandler.cs:71` — *before* the guard runs — so the shared `ApplicationDbContext`'s tracked instance legitimately carries the rejected code, and a **correct** implementation would fail that assertion. Assert the response only. If persistence must be asserted, re-read through a **second `ApplicationDbContext` bound to the same InMemory database name**, before any further `SaveChangesAsync` on the original context; to do that, capture the database name into a field instead of inlining it at line 34. Production is unaffected — the context is request-scoped and no `IPipelineBehavior` in `Application/Common/Behaviors/` calls `SaveChanges`.
- **Amendment A6:** `New` joining the code-occupying set is verified harmless. `AssignBoxCodeIfAny` (the only writer of `Code` on a `New` box) is called once, at `ChangeTransportBoxStateHandler.cs:71`, always followed in the same unit of work by `Open(...)` which moves the box to `Opened`; `Reset(...)` nulls `Code` on the `Opened → New` return. No path persists a `New` box carrying a code. Do not add a test that depends on one existing.
- Transitions available for building fixtures: `Quarantine` = `Open(code, date, user)` then `ToQuarantine(date, user)`; `Error` = `Open(code, date, user)` then `Error(date, user, message)` (`Error` accepts any source state — `CheckState` no-ops on `Array.Empty<TransportBoxState>()`).

#### Implementation steps

- [ ] **Step 1: Add the two FR-3 tests**

```csharp
[Fact]
public async Task OpenTransportBox_WhenCodeHeldByQuarantinedBox_ShouldPreventDuplicate()
{
    // Arrange — an existing box holds B001 in Quarantine.
    var quarantined = new TransportBox();
    quarantined.Open("B001", DateTime.UtcNow, TestUser);
    quarantined.ToQuarantine(DateTime.UtcNow, TestUser);
    await _repository.AddAsync(quarantined);
    await _repository.SaveChangesAsync();

    var freshBox = new TransportBox();
    await _repository.AddAsync(freshBox);
    await _repository.SaveChangesAsync();

    // Act
    var result = await _handler.Handle(new ChangeTransportBoxStateRequest
    {
        BoxId = freshBox.Id,
        NewState = TransportBoxState.Opened,
        BoxCode = "B001"
    }, CancellationToken.None);

    // Assert — response only (amendment A3: do NOT assert the tracked box's Code here).
    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.TransportBoxDuplicateActiveBoxFound);
    result.Params.Should().ContainKey("code").WhoseValue.Should().Be("B001");
}
```

Add the `Error` twin, `OpenTransportBox_WhenCodeHeldByErroredBox_ShouldPreventDuplicate`, built with `Open("B001", ...)` then `Error(DateTime.UtcNow, TestUser, "boom")`.

- [ ] **Step 2 (optional, only if persistence assertion is wanted): capture the InMemory database name**

Change line 34's inlined `Guid.NewGuid().ToString()` into a `private readonly string _databaseName = Guid.NewGuid().ToString();` field used by the options builder, then construct a second `ApplicationDbContext` on the same name inside the test to re-read the box. This is the **only** sanctioned way to assert persistence here. If you skip this step, do not assert persistence at all.

- [ ] **Step 3: Run the file and confirm the two new tests fail before the repository fix, pass after**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxUniquenessTests"
```

Expected after `consume-rule-in-transport-box-repository` has landed: **all** PASS — the two new ones plus the five pre-existing ones, none of which may be modified.

- [ ] **Step 4: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- Assigning code `B001` to a `New` box while another box holds `B001` in `Quarantine` returns `Success = false`, `ErrorCode = TransportBoxDuplicateActiveBoxFound`, `Params["code"] == "B001"`.
- Same for a box holding `B001` in `Error`.
- No assertion anywhere in the file claims the rejected `New` box's `Code` is `null` on the *original* context's tracked instance (amendment A3).
- All five pre-existing tests pass **unmodified**, in particular `OpenTransportBoxWithCodeThenCloseItThenOpenAnotherWithSameCode_ShouldSucceed` (a `Closed` box still frees the code) and `OpenTwoTransportBoxesWithDifferentCodes_ShouldSucceed` (no self-match against the in-flight box).
- `ChangeTransportBoxStateHandler.cs` is **not** modified by this task.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxUniquenessTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

The second must pass unmodified — it mocks `ITransportBoxRepository`, so the repository change is invisible to it.

---

