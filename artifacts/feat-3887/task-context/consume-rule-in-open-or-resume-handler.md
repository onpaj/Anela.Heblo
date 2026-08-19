### task: consume-rule-in-open-or-resume-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` — line 62 predicate, lines 68-69 comment
- Modify (test): `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/OpenOrResumeBoxByCodeHandlerTests.cs`

**Depends on:** `add-transport-box-state-rules`. Independent of the repository task (the handler test mocks `ITransportBoxRepository`), but the corrected comment describes behaviour that `consume-rule-in-transport-box-repository` delivers.

#### Goal

De-duplicate the handler's inline deny-list into the shared rule (FR-4) and correct the stale comment that documents a guarantee the code did not previously provide (amendment A4). This is a **pure de-duplication** — behaviour is byte-for-byte identical for every current enum value.

#### Context

Current code (`OpenOrResumeBoxByCodeHandler.cs:61-69`):

```csharp
            // A box with this code is busy in a non-resumable state.
            if (existing != null && existing.State != TransportBoxState.Closed && existing.State != TransportBoxState.Stocked)
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }

            // No box, or only a Closed/Stocked box with this code — create and open a fresh one.
            // GetByCodeAsync returns any active box first, so reaching here means none exists.
```

- The three-branch structure must be preserved verbatim: (1) `existing.State == Opened` → resume (line 52), (2) code occupied → `TransportBoxDuplicateActiveBoxFound` with `code` and `state` params, (3) otherwise → create and open a fresh box. The `code` normalisation at line 44 and every `catch` block are unchanged.
- The comment at line 69 — *"GetByCodeAsync returns any active box first, so reaching here means none exists"* — is **false today** and becomes true only because of the `GetByCodeAsync` re-ordering in `consume-rule-in-transport-box-repository`. Amendment A4 requires restating it so it names its source of truth.
- The cascade this closes, all three steps reachable today: box #5 sits in `Quarantine` holding `B001`; an operator assigns `B001` to box #20 from the admin UI (the reported bug) and #20 runs through to `Stocked`; a terminal scan of `B001` ranks #5 and #20 equally (neither is `Closed`), `Id` desc picks #20, and branch 3 mints a **third** row holding `B001`.
- `ErrorCodes.TransportBoxDuplicateActiveBoxFound` is 1405 (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:153`), already mapped in `frontend/src/i18n.ts:155-156` — no frontend change.

#### Implementation steps

- [ ] **Step 1: Replace the inline deny-list (line 62)**

```csharp
            // A box with this code is busy in a non-resumable state.
            if (existing != null && TransportBoxStateRules.OccupiesCode(existing.State))
            {
                return new OpenOrResumeBoxByCodeResponse(ErrorCodes.TransportBoxDuplicateActiveBoxFound,
                    new Dictionary<string, string> { { "code", code }, { "state", existing.State.ToString() } });
            }
```

`using Anela.Heblo.Domain.Features.Logistics.Transport;` is already present (line 4).

- [ ] **Step 2: Correct the comment (amendment A4)**

```csharp
            // No box, or only a Closed/Stocked box with this code — create and open a fresh one.
            // GetByCodeAsync orders on TransportBoxStateRules.OccupiesCodePredicate, so any
            // code-occupying box outranks a released one; reaching here means none exists.
```

- [ ] **Step 3: Extend `OpenOrResumeBoxByCodeHandlerTests`**

Add these builders alongside the existing `OpenedBox` / `ClosedBox` / `InTransitBox` / `StockedBox` helpers (transitions verified against `TransportBox.cs`):

```csharp
private static TransportBox ReceivedBox(string code)    { var b = InTransitBox(code); b.Receive(FixedTime, "Test User"); return b; }
private static TransportBox ReserveBox(string code)     { var b = OpenedBox(code); b.ToReserve(FixedTime, "Test User", "L1"); return b; }
private static TransportBox QuarantineBox(string code)  { var b = OpenedBox(code); b.ToQuarantine(FixedTime, "Test User"); return b; }
private static TransportBox ErrorBox(string code)       { var b = OpenedBox(code); b.Error(FixedTime, "Test User", "boom"); return b; }
```

Add busy-state coverage for `Quarantine`, `Error`, `Reserve` and `Received` alongside the existing `Handle_BoxBusyInTransit_ReturnsDuplicateActiveBoxFound`. Each mocks `GetByCodeAsync("B001")` to return the corresponding box and asserts:

- `result.Success` is `false`
- `result.ErrorCode == ErrorCodes.TransportBoxDuplicateActiveBoxFound`
- `result.Params["state"]` equals that state's `ToString()` (`"Quarantine"`, `"Error"`, `"Reserve"`, `"Received"`)
- `result.Params["code"] == "B001"`
- `AddAsync` and `SaveChangesAsync` are each verified `Times.Never`

Plus the **A4 cascade test**: with `GetByCodeAsync` mocked to return the `Quarantine` box — which is what the fixed repository now returns for a code shared by a lower-`Id` `Quarantine` box and a higher-`Id` `Stocked` box — the handler returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"] == "Quarantine"` and creates nothing. Name it so its intent survives, e.g. `Handle_QuarantineBoxResolvedOverNewerStockedBox_DoesNotMintThirdBox`.

Do **not** modify any existing test in this file — FR-4 is a pure de-duplication and the existing suite passing unchanged is the proof.

- [ ] **Step 4: Run the handler tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"
```

Expected: all PASS, old and new. The new busy-state tests pass both before and after the line-62 edit — that is the point; they pin the equivalence.

- [ ] **Step 5: Build and format**

```bash
cd backend && dotnet build && dotnet format
```

#### Acceptance criteria

- Scanning a code held by a `Quarantine`, `Error`, `Reserve`, `Received` or `InTransit` box returns `TransportBoxDuplicateActiveBoxFound` with `Params["state"]` set to that state's name and `Params["code"]` set to the normalised code; no box is created.
- Scanning a code held only by a `Closed` or `Stocked` box creates and opens a new box (`Resumed == false`).
- Scanning a code held by an `Opened` box resumes it (`Resumed == true`) and calls neither `AddAsync` nor `SaveChangesAsync`.
- Every pre-existing test in `OpenOrResumeBoxByCodeHandlerTests` passes **unmodified**.
- `grep -n "TransportBoxState.Closed\|TransportBoxState.Stocked" backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/OpenOrResumeBoxByCode/OpenOrResumeBoxByCodeHandler.cs` returns nothing (the `Opened` comparison at line 52 stays — it is the resume branch, not the occupancy rule).
- The line-68/69 comment names `TransportBoxStateRules.OccupiesCodePredicate` as the source of the ordering guarantee.
- `dotnet build` and `dotnet format` succeed with no new warnings.

#### Tests to run

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OpenOrResumeBoxByCodeHandlerTests"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBox&Category!=Integration"
```

---

