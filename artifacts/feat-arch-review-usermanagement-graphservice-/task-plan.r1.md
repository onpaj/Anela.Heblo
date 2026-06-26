# GraphService.GetGroupMembersAsync Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract two private helpers (`AcquireGraphTokenAsync`, `ParseMembersFromJson`) from the 160-line `GraphService.GetGroupMembersAsync` so the orchestrator drops to ≤50 lines while preserving every observable behaviour (cache, token, HTTP, logging, exception flow).

**Architecture:** Strictly internal refactor inside `Features/UserManagement/Services/GraphService.cs`. Token helper is `private async Task<string>` instance method (uses `_tokenAcquisition` and `_logger`). Parser helper is `internal static (List<UserDto> Users, int TotalCount)` to enable direct unit tests via `InternalsVisibleTo`. Orchestrator still owns cache, HTTP send, and outer `MsalException` catch+rethrow. No public surface change. No new DI. No new files except a single new test class.

**Tech Stack:** C# / .NET (Anela.Heblo.Application), `System.Text.Json.JsonDocument`, `Microsoft.Identity.Web`, `Microsoft.Extensions.Caching.Memory`, xUnit + FluentAssertions + Moq for tests.

**Specification Amendments Applied (from arch-review.r1.md):**
1. `AcquireGraphTokenAsync` returns `Task<string>` (not `Task<string?>`). Does NOT catch `MsalException` — the outer catch in the orchestrator continues to log+rethrow as it does today.
2. `ParseMembersFromJson` returns `(List<UserDto> Users, int TotalCount)` so the orchestrator can log the existing aggregate "{TotalMembers} total / {UserMembers} user" message without the parser depending on `ILogger` or `groupId`.
3. `ParseMembersFromJson` is `internal static` (not `private static`) so unit tests in `Anela.Heblo.Tests` can reach it via `InternalsVisibleTo`. This is not a public-surface change.
4. Per-element `LogDebug` lines inside the existing parsing loop are removed. Aggregate counts remain (logged by orchestrator using the parser's tuple return).

---

## File Structure

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — extract two helpers, shrink orchestrator.
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` **OR** `backend/src/Anela.Heblo.Application/Properties/AssemblyInfo.cs` — add `InternalsVisibleTo("Anela.Heblo.Tests")` ONLY IF it does not already exist.

**New files:**
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs` — direct unit tests for the parser helper.

**Unchanged but load-bearing (must still pass without edits):**
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — full behaviour safety net.
- All callers of `IGraphService.GetGroupMembersAsync`, especially `GraphService.GetAppRoleMembersAsync` (same file, line 382) which calls the orchestrator to expand nested group members.

---

## Task 1: Establish Baseline and Verify Prerequisites

**Files:**
- Read: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`
- Read: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
- Read: `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

- [ ] **Step 1: Capture green baseline for the affected tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: All `GraphServiceTests` pass. Record the exact count of passing tests (e.g. "7 passed, 0 failed") in a scratch note — this is the post-refactor target.

- [ ] **Step 2: Locate the exact line ranges in `GraphService.cs` that will move**

Use Read on `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`. In a scratch note, record:
- Cache TryGetValue block (around lines 39–45).
- Token acquisition block including stopwatch, scope literal, `LogInformation` "Attempting…", call to `_tokenAcquisition.GetAccessTokenForAppAsync`, "Successfully acquired… in {Duration}ms", "Token acquired with length: {TokenLength}" (around lines 64–85).
- HTTP send block (around lines 89–113) — STAYS in orchestrator.
- JSON parse block including `JsonDocument.Parse`, foreach over `value`, `@odata.type` discrimination, per-element `LogDebug` lines, aggregate `LogInformation` "Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}" (around lines 117–164).
- Cache Set block (around lines 171–173).
- Outer `catch (MsalException msalEx)` block (around line 168) — STAYS in orchestrator.

Confirm the actual line numbers in your local copy (the spec/arch-review cite approximate ranges; trust the source).

- [ ] **Step 3: Verify `InternalsVisibleTo` for the test assembly**

Run:
```bash
grep -R "InternalsVisibleTo" backend/src/Anela.Heblo.Application/
```

Expected outcomes:
- (A) A line containing `InternalsVisibleTo` AND `Anela.Heblo.Tests` already exists → skip Task 2 entirely.
- (B) `InternalsVisibleTo` exists but does NOT name `Anela.Heblo.Tests` → Task 2 will add the entry alongside the existing one.
- (C) No `InternalsVisibleTo` anywhere → Task 2 will add a fresh declaration in the `.csproj`.

Record which outcome applies before proceeding.

- [ ] **Step 4: Capture the existing token scope literal verbatim**

In the token acquisition block (around line 64–85), copy the exact scope string passed to `_tokenAcquisition.GetAccessTokenForAppAsync(...)` into the scratch note. The helper introduced in Task 5 must pass the same literal — do not paraphrase or normalise it.

- [ ] **Step 5: Capture the existing log message templates verbatim**

From the same range, copy verbatim into the scratch note:
- The "Attempting…" template and its log level.
- The "Successfully acquired…" template and level.
- The "Token acquired with length…" template and level.
- The "Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}" template and level.

These templates and levels must be reproduced byte-for-byte (FR-1 / spec acceptance criteria).

- [ ] **Step 6: Inspect callers of `GetGroupMembersAsync` to confirm signature constraints**

Run:
```bash
grep -Rn "GetGroupMembersAsync" backend/src/
```

Expected: `GetAppRoleMembersAsync` in the same file (around line 382) is one of the callers. Confirm there is no caller that depends on a behaviour we are about to change (there shouldn't be — this refactor preserves behaviour). Record the caller list in the scratch note.

- [ ] **Step 7: No commit for this task**

This task is read-only baseline capture. Nothing to commit.

---

## Task 2: Add `InternalsVisibleTo("Anela.Heblo.Tests")` (only if missing)

**Skip this entire task if Task 1 Step 3 returned outcome (A).**

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

- [ ] **Step 1: Open the project file and locate the `<ItemGroup>` containing existing references**

Use Read on `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`. Identify either:
- An existing `<ItemGroup>` containing `<InternalsVisibleTo …/>` entries (outcome B), or
- The last `<ItemGroup>` in the file (outcome C).

- [ ] **Step 2: Add the entry**

For outcome B (add alongside existing entries), use Edit to insert a sibling line. Example resulting fragment:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="Anela.Heblo.SomeExistingAssembly" />
  <InternalsVisibleTo Include="Anela.Heblo.Tests" />
</ItemGroup>
```

For outcome C (no existing block), add a new `<ItemGroup>` near the bottom of the `<Project>` element:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="Anela.Heblo.Tests" />
</ItemGroup>
```

- [ ] **Step 3: Restore and build to confirm the project still compiles**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors. Warnings unchanged from baseline.

- [ ] **Step 4: Re-run the baseline tests to confirm nothing regressed**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests" --no-restore
```

Expected: Same pass count as Task 1 Step 1.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
git commit -m "chore: expose internals of Anela.Heblo.Application to Anela.Heblo.Tests"
```

---

## Task 3: Write Failing Characterization Tests for `ParseMembersFromJson`

The parser does not exist yet. We write tests against the intended `internal static` signature first; they will fail to compile until Task 4. The tests describe behaviour the current inline parser already exhibits — we are pinning that behaviour down before moving the code.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs`

- [ ] **Step 1: Inspect `UserDto` so the test fixtures use real field names**

Use Glob to find `UserDto.cs`:
```bash
grep -Rn "class UserDto" backend/src/Anela.Heblo.Application/
```

Read the file. Record the public property names (the arch-review references `Id`, `DisplayName`, `Email`; verify in the source — DO NOT assume).

- [ ] **Step 2: Inspect the existing parsing block in `GraphService.cs` to capture exact mapping rules**

From the parsing range identified in Task 1 Step 2, record in the scratch note:
- How `id` is read and what default it falls back to.
- How `displayName` is read and what default it falls back to.
- How `mail` is read.
- How `userPrincipalName` is read.
- The exact rule for deciding whether an entry maps to `Email`: today's code prefers `mail`, falls back to `userPrincipalName`, otherwise empty string — confirm in source.
- The exact rule for the `@odata.type` user/group discrimination — what string comparison the existing code does (e.g. `Contains("user")`, `Equals("#microsoft.graph.user")`, case-sensitivity).
- Whether an entry that lacks `@odata.type` but has `userPrincipalName` is treated as a user today.

Match the test expectations to the source's current behaviour, NOT to the spec's prose. The whole point of the refactor is to preserve current behaviour exactly.

- [ ] **Step 3: Create the test file with failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs`. Use the existing `GraphServiceTests.cs` for the namespace + using conventions; mirror them. Suggested content (adjust `UserDto` property names and the `@odata.type` discrimination rule to match what Step 2 recorded):

```csharp
using System.Collections.Generic;
using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class ParseMembersFromJsonTests
{
    [Fact]
    public void ReturnsEmptyList_WhenJsonHasNoValueProperty()
    {
        var json = "{}";

        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        users.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public void ReturnsEmptyList_WhenValueArrayIsEmpty()
    {
        var json = """{"value":[]}""";

        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        users.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public void MapsSingleUser_WithAllFieldsPresent()
    {
        var json = """
        {
          "value": [
            {
              "@odata.type": "#microsoft.graph.user",
              "id": "u-1",
              "displayName": "Alice Example",
              "mail": "alice@example.com",
              "userPrincipalName": "alice@example.onmicrosoft.com"
            }
          ]
        }
        """;

        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        users.Should().HaveCount(1);
        users[0].Id.Should().Be("u-1");
        users[0].DisplayName.Should().Be("Alice Example");
        users[0].Email.Should().Be("alice@example.com");
        totalCount.Should().Be(1);
    }

    [Fact]
    public void FallsBackToUserPrincipalName_WhenMailIsMissing()
    {
        var json = """
        {
          "value": [
            {
              "@odata.type": "#microsoft.graph.user",
              "id": "u-2",
              "displayName": "Bob NoMail",
              "userPrincipalName": "bob@example.onmicrosoft.com"
            }
          ]
        }
        """;

        var (users, _) = GraphService.ParseMembersFromJson(json);

        users.Should().HaveCount(1);
        users[0].Email.Should().Be("bob@example.onmicrosoft.com");
    }

    [Fact]
    public void SkipsGroupEntries_AndCountsThemInTotal()
    {
        var json = """
        {
          "value": [
            { "@odata.type": "#microsoft.graph.user",  "id": "u-3", "displayName": "Carol", "mail": "carol@example.com" },
            { "@odata.type": "#microsoft.graph.group", "id": "g-1", "displayName": "Nested Group" },
            { "@odata.type": "#microsoft.graph.user",  "id": "u-4", "displayName": "Dan",   "mail": "dan@example.com" }
          ]
        }
        """;

        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        users.Should().HaveCount(2);
        users.Select(u => u.Id).Should().BeEquivalentTo(new[] { "u-3", "u-4" });
        totalCount.Should().Be(3);
    }

    [Fact]
    public void TolerateMissingOptionalFields_OnUserEntries()
    {
        var json = """
        {
          "value": [
            { "@odata.type": "#microsoft.graph.user", "id": "u-5" }
          ]
        }
        """;

        var (users, totalCount) = GraphService.ParseMembersFromJson(json);

        users.Should().HaveCount(1);
        users[0].Id.Should().Be("u-5");
        users[0].DisplayName.Should().Be(string.Empty);
        users[0].Email.Should().Be(string.Empty);
        totalCount.Should().Be(1);
    }
}
```

If the current source treats an entry with no `@odata.type` but a `userPrincipalName` as a user (a defensive fallback), add a seventh test asserting that behaviour. If it strictly requires `@odata.type` containing "user", omit it. Decide based on Step 2 evidence.

- [ ] **Step 4: Build the test project to confirm tests fail at compile time**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build FAILS with `CS0117: 'GraphService' does not contain a definition for 'ParseMembersFromJson'` (or similar). This proves the new tests reference a method that has not yet been written — the RED step of TDD.

- [ ] **Step 5: No commit yet**

We do not commit a broken build. Task 4 will add the parser and bring the tree back to green before commit.

---

## Task 4: Extract `ParseMembersFromJson` and Make Tests Green

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

- [ ] **Step 1: Add the parser as `internal static`, mirroring the existing inline logic**

Insert the new method as a private region near the bottom of the `GraphService` class. The method body is a direct transplant of the existing parsing block (Task 1 Step 2 range, around lines 117–164), with two changes:
1. It takes a `string json` parameter instead of reading `responseBody` from an outer scope.
2. It returns a tuple `(List<UserDto> Users, int TotalCount)` instead of writing into a list visible to the orchestrator.
3. The per-element `LogDebug` calls inside the loop are deleted. No `ILogger` is referenced inside the parser.

Skeleton — fill in the field-extraction lines using the exact null-handling that the current inline code does (do not invent defaults that the source doesn't already use):

```csharp
internal static (List<UserDto> Users, int TotalCount) ParseMembersFromJson(string json)
{
    var users = new List<UserDto>();
    var totalCount = 0;

    using var doc = JsonDocument.Parse(json);

    if (!doc.RootElement.TryGetProperty("value", out var valueArray)
        || valueArray.ValueKind != JsonValueKind.Array)
    {
        return (users, totalCount);
    }

    foreach (var member in valueArray.EnumerateArray())
    {
        totalCount++;

        // === BEGIN: copy of existing user/group discrimination from GraphService.cs lines ~130–155 ===
        // Paste the existing @odata.type check verbatim (case sensitivity, Contains vs Equals, fallback rules).
        // If the entry is NOT a user, `continue;` without adding to `users`.
        // If it IS a user, extract id / displayName / mail / userPrincipalName using the SAME null-handling
        // that the inline code uses today, build a UserDto, and `users.Add(...)` it.
        // === END ===
    }

    return (users, totalCount);
}
```

When you transplant the field-extraction lines, do not change `string?` vs `string`, do not change defaults, do not change ordering, and do not replace `foreach`/index loops with LINQ (NFR-1 forbids allocation regressions).

- [ ] **Step 2: Update the orchestrator to call the parser instead of parsing inline**

Within `GetGroupMembersAsync`, replace the parsing block (Task 1 Step 2 range) with a single call:

```csharp
var (members, totalMembers) = ParseMembersFromJson(responseBody);
_logger.LogInformation(
    "Processed {TotalMembers} total members, {UserMembers} user members for group {GroupId}",
    totalMembers, members.Count, groupId);
```

The template, level, and property names of that `LogInformation` MUST match what Task 1 Step 5 recorded. Do not paraphrase.

The per-element `LogDebug` lines that lived inside the loop are intentionally dropped (spec amendment #4 from arch-review). Do not re-add them in the orchestrator.

- [ ] **Step 3: Build the production project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded. If there are unresolved references (e.g. missing `using System.Text.Json;`), add them.

- [ ] **Step 4: Build the test project**

Run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: Build succeeded. The `ParseMembersFromJson` reference in the new test file now resolves thanks to `InternalsVisibleTo`.

- [ ] **Step 5: Run the new parser tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ParseMembersFromJsonTests" --no-restore
```

Expected: All tests in `ParseMembersFromJsonTests` pass. If a test fails, the parser is not faithfully reproducing the inline behaviour — fix the parser (do NOT loosen the test) until they all pass.

- [ ] **Step 6: Run the existing `GraphServiceTests` to confirm the orchestrator still works end-to-end**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests" --no-restore
```

Expected: Same pass count as Task 1 Step 1 — zero regressions.

- [ ] **Step 7: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
  backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs
git commit -m "refactor(graph): extract ParseMembersFromJson helper from GetGroupMembersAsync"
```

---

## Task 5: Extract `AcquireGraphTokenAsync`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`

- [ ] **Step 1: Add the helper as `private async Task<string>` instance method**

Insert near the other private helpers (alongside `ParseMembersFromJson` is fine). The body is a verbatim transplant of the existing token-acquisition block (Task 1 Step 2 range, around lines 64–85), with one change: it takes `string groupId` and `CancellationToken cancellationToken` as parameters so the helper can log `groupId` if any current template references it (Task 1 Step 5 will tell you whether it does).

```csharp
private async Task<string> AcquireGraphTokenAsync(string groupId, CancellationToken cancellationToken)
{
    // Paste verbatim from the current implementation:
    //   - the scope literal (captured in Task 1 Step 4)
    //   - _logger.LogInformation("Attempting to acquire MS Graph token with application scope: {Scope}", ...)
    //   - var stopwatch = Stopwatch.StartNew();
    //   - var token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);   // <-- DO NOT wrap in try/catch
    //   - stopwatch.Stop();
    //   - _logger.LogInformation("Successfully acquired MS Graph application token in {Duration}ms", stopwatch.ElapsedMilliseconds);
    //   - _logger.LogDebug("Token acquired with length: {TokenLength}", token.Length);
    //   - return token;
}
```

CRITICAL invariants:
- Do NOT add a `try { … } catch (MsalException) { return null; }` block. The current code lets `MsalException` propagate to the outer catch in the orchestrator, which logs and rethrows. The spec FR-1 prose is inaccurate on this point (arch-review amendment #1) — follow the source, not the spec.
- Do NOT change log message templates, log levels, or structured property names. They must remain byte-for-byte identical (FR-1 acceptance criterion).
- Do NOT change the scope literal.
- Use the `cancellationToken` parameter if the existing call does (e.g. some overloads accept a token); otherwise do not invent one.

- [ ] **Step 2: Update the orchestrator to call the helper instead of inlining the token logic**

Within `GetGroupMembersAsync`, replace the entire token-acquisition block with one call:

```csharp
var graphToken = await AcquireGraphTokenAsync(groupId, cancellationToken);
```

Keep the existing outer `try { … } catch (MsalException msalEx) { … }` wrapping intact — its catch block continues to handle MSAL failures thrown from inside the helper. Do not move the catch into the helper.

Keep the existing HTTP request construction (Bearer header, URI, method) inline after the helper call. The HTTP send block is explicitly out of scope (arch-review Decision 3).

- [ ] **Step 3: Build the production project**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded with no new warnings.

- [ ] **Step 4: Run the full `GraphServiceTests` fixture**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GraphServiceTests" --no-restore
```

Expected: Same pass count as Task 1 Step 1. Specifically:
- `GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory` — still passes (cache short-circuit preserved; helper not entered).
- `GetGroupMembersAsync_TokenAcquisitionMsalException_Throws` (or equivalent) — still passes (MSAL still propagates and the outer catch still rethrows).
- All response-shape tests — still pass with the same `FakeHttpMessageHandler` payloads.

If any test fails, the most likely cause is a log-template drift or accidentally catching `MsalException` inside the helper. Diff against the pre-refactor code and align.

- [ ] **Step 5: Run the parser tests as well to confirm no regression**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ParseMembersFromJsonTests" --no-restore
```

Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs
git commit -m "refactor(graph): extract AcquireGraphTokenAsync helper from GetGroupMembersAsync"
```

---

## Task 6: Final Orchestrator Cleanup and Acceptance Verification

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` (only if cleanup needed)

- [ ] **Step 1: Read the post-refactor `GetGroupMembersAsync` and count its lines**

Run:
```bash
grep -n "GetGroupMembersAsync\|^    }\|^    public\|^    private" \
  backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs | head -40
```

Then Read the method body. Confirm:
- Line count is ≤50 (target ~30) — FR-3 acceptance.
- The method reads as a sequential pipeline: cache read → groupId validation → `AcquireGraphTokenAsync` → HTTP send → `ParseMembersFromJson` → aggregate log → cache write → return.
- The outer `try { … } catch (MsalException) { … }` wrapping is still present and still rethrows after logging.
- The HTTP request construction (URI, headers, method) and `IsSuccessStatusCode` handling remain inline (FR-3 keeps these inline — out of scope to extract).
- No new `internal` or `public` members exist besides `ParseMembersFromJson` (which is intentionally internal for test access).

If the method is still >50 lines, the most likely culprits are stale comments or a redundant variable; tidy those without changing behaviour. Do not aggressively reformat unrelated code.

- [ ] **Step 2: Diff-check log statements against the pre-refactor baseline**

Run:
```bash
git diff HEAD~2 -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
  | grep -E "^[-+].*Log(Information|Warning|Error|Debug|Trace|Critical)"
```

Inspect every `-`/`+` log line in the diff. The only legitimate differences should be:
- Lines moved into `AcquireGraphTokenAsync` or `ParseMembersFromJson` (location change only).
- The two per-element `LogDebug` lines inside the parsing loop, intentionally removed (arch-review amendment #4).
- The aggregate "Processed {TotalMembers} total members…" line now lives in the orchestrator (template unchanged).

Any other template/level/property-name change is a regression — fix it.

- [ ] **Step 3: Confirm no token values or response bodies are logged**

Run:
```bash
git diff HEAD~2 -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
  | grep -iE "accessToken|Authorization|responseBody|response\.Content"
```

Expected: The only matches are the existing `responseBody = await response.Content.ReadAsStringAsync(...)` line (which was already there) and the Bearer header construction (already there). No `LogXxx(...)` line includes `accessToken`, the `Authorization` header value, or the raw response body string. NFR-2 acceptance.

- [ ] **Step 4: Confirm `IGraphService` and `UserDto` are untouched**

Run:
```bash
git diff HEAD~2 -- backend/src/Anela.Heblo.Application/Features/UserManagement/Contracts/ \
  backend/src/Anela.Heblo.Application/Features/UserManagement/Models/
```

(Adjust paths if `IGraphService.cs` / `UserDto.cs` live elsewhere — use `grep -Rn "interface IGraphService" backend/src/` first if unsure.)

Expected: No diff. FR-3 + spec "API / Interface Design" acceptance.

- [ ] **Step 5: Confirm no new public members on `GraphService`**

Run:
```bash
git diff HEAD~2 -- backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
  | grep -E "^\+\s*(public|protected)\s+"
```

Expected: No matches. The only additions should be `private` and one `internal static` (the parser).

- [ ] **Step 6: Run the full test suite for the Application project**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore
```

Expected: All tests pass. Any failure outside `GraphServiceTests` / `ParseMembersFromJsonTests` is suspicious — investigate before claiming completion. Pay special attention to any test that exercises `GetAppRoleMembersAsync` (the nested-group expansion path that calls `GetGroupMembersAsync` from line 382 of the same file).

- [ ] **Step 7: Run the full solution build to catch any cross-project effects**

Run:
```bash
dotnet build
```

Expected: Build succeeded, 0 errors. Warning count unchanged from baseline (Task 1 implicit).

- [ ] **Step 8: Verify line-coverage of the parser is ≥90% (NFR-3)**

Run (adjust collector configuration if the repo already specifies one — search for `coverlet.runsettings` or `.runsettings` first):
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ParseMembersFromJsonTests" \
  --collect:"XPlat Code Coverage"
```

Inspect the generated coverage report (Cobertura XML under `TestResults/.../coverage.cobertura.xml`). Locate the `ParseMembersFromJson` method entry. Confirm line-rate ≥ 0.90. If below, add a test case that covers the missing branch (likely the "value missing entirely" branch or the group-vs-user discrimination edge case).

- [ ] **Step 9: Optional micro-benchmark (NFR-1)**

If the project already has a benchmark harness (search for `BenchmarkDotNet` references), add a comparison run for `ParseMembersFromJson` vs the pre-refactor inline parse on a synthetic 100-member response. Confirm ≤5% deviation in execution time and allocated bytes.

If no benchmark harness exists, this step is satisfied by visual inspection: confirm the parser uses the same loop shape (no LINQ, no extra `List` allocations, no extra `ToList()` calls) as the pre-refactor inline code. Record the inspection result in the commit body.

- [ ] **Step 10: Final commit (only if Step 1 required cleanup or Step 8 added a test)**

If no further code changes were needed in this task, skip the commit. Otherwise:

```bash
git add backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs \
        backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs
git commit -m "refactor(graph): final polish on GetGroupMembersAsync orchestrator"
```

---

## Acceptance Checklist (verify before closing the work)

- [ ] `GetGroupMembersAsync` method body ≤50 lines (target ~30). [FR-3]
- [ ] `GetGroupMembersAsync` signature, return type, parameter list, and `CancellationToken` propagation unchanged. [FR-3]
- [ ] Cache read short-circuits before token / HTTP / parse; cache write happens only after a successful parse. [FR-4]
- [ ] `AcquireGraphTokenAsync` is `private async Task<string>` instance method, does not catch `MsalException`. [FR-1, arch-review amendment #1]
- [ ] `ParseMembersFromJson` is `internal static (List<UserDto>, int)` and contains no `ILogger` dependency. [FR-2, arch-review amendment #2/#3]
- [ ] No new public/protected members on `GraphService`. [FR-3]
- [ ] `IGraphService` and `UserDto` unchanged. [Spec API section]
- [ ] All pre-existing `GraphServiceTests` pass without modification. [FR-3]
- [ ] New `ParseMembersFromJsonTests` covers: empty value, missing value, all-users, mixed users+groups, missing optional fields, mail/upn fallback. Line coverage ≥90%. [NFR-3]
- [ ] Log message templates, levels, and structured property names for "Attempting…", "Successfully acquired…", "Token acquired with length…", and "Processed {TotalMembers}…" are byte-for-byte identical to pre-refactor. [FR-1, NFR-2]
- [ ] No log statement emits `accessToken`, `Authorization` header values, or raw response body strings. [NFR-2]
- [ ] Per-element `LogDebug` lines inside the old parsing loop are removed (intentional, per arch-review amendment #4).
- [ ] `InternalsVisibleTo("Anela.Heblo.Tests")` is declared on `Anela.Heblo.Application` (added in Task 2 if it was not already present).
- [ ] Full solution build succeeds; full `Anela.Heblo.Tests` run succeeds.
- [ ] `GetAppRoleMembersAsync` (caller at line 382 of the same file) behaviour visually inspected and existing tests covering it (if any) pass.

---

## Self-Review Notes

**Spec coverage:**
- FR-1 (`AcquireGraphTokenAsync` extraction) → Task 5.
- FR-2 (`ParseMembersFromJson` extraction) → Tasks 3+4.
- FR-3 (orchestrator rewrite ≤50 lines, signature preserved) → Task 6 Steps 1, 4, 5.
- FR-4 (caching semantics preserved) → enforced in Tasks 4/5/6, verified via existing `GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory`.
- NFR-1 (no perf regression) → Task 6 Step 9.
- NFR-2 (no security regression, no token/body logging) → Task 6 Step 3.
- NFR-3 (parser unit-testable, ≥90% line coverage) → Task 3 (tests) + Task 6 Step 8 (coverage check).
- NFR-4 (no method >50 lines) → Task 6 Step 1.

**Arch-review amendments applied:**
- Amendment #1 (token returns `Task<string>`, MSAL not caught in helper) → enforced in Task 5 Step 1's CRITICAL note.
- Amendment #2 (parser returns tuple) → Task 3 tests + Task 4 implementation.
- Amendment #3 (parser is `internal static` + `InternalsVisibleTo`) → Task 2 + Task 4 Step 1.
- Amendment #4 (per-element `LogDebug` lines removed) → Task 4 Step 1 (deletion) + Task 6 Step 2 (diff verification).

**Risks mitigated (from arch-review):**
- HIGH risk: `GetAppRoleMembersAsync` silent regression → Task 6 Step 6 explicitly checks this caller.
- HIGH risk: misreading spec's "returns null on MsalException" → Task 5 Step 1 CRITICAL note and Task 1 Step 5 capture of current behaviour.
- MEDIUM risk: logging drift → Task 1 Step 5 baseline + Task 6 Step 2 diff check.
- MEDIUM risk: parser test access via wrong visibility → Task 2 (only adds `InternalsVisibleTo`; no public widening).
- MEDIUM risk: aggregate log coupling parser to `ILogger`/`groupId` → tuple return resolves this.
- LOW risks (cache short-circuit, LINQ allocations) → Task 4 Step 6 and Task 6 Step 9.
