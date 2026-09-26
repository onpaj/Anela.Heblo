# Remove non-zero LastModified defaults on Dashboard domain entities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the `= DateTime.UtcNow` property initializers from `UserDashboardTile.LastModified` and `UserDashboardSettings.LastModified` so both default to `DateTime.MinValue`, matching the project's documented `TimeProvider`-based entity-timestamp convention (`docs/architecture/Dev_Guidelines_time.md`).

**Architecture:** No architectural change — this is a conformance fix to an existing documented convention. Two domain entity properties in `Anela.Heblo.Domain.Features.Dashboard` lose their non-zero default; every production call site already assigns `LastModified` explicitly via `TimeProvider` before persistence, so no handler, mutator, EF Core mapping, or DTO changes.

**Tech Stack:** .NET 8 / C#, xUnit, FluentAssertions, `Anela.Heblo.Domain` / `Anela.Heblo.Tests` projects.

---

### task: remove-lastmodified-defaults

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs:11`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs:8`
- Create: `backend/test/Anela.Heblo.Tests/Domain/Dashboard/UserDashboardEntityDefaultsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Domain/Dashboard/UserDashboardEntityDefaultsTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Dashboard;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Dashboard;

public class UserDashboardEntityDefaultsTests
{
    [Fact]
    public void UserDashboardTile_Constructed_DefaultsLastModifiedToMinValue()
    {
        var tile = new UserDashboardTile();

        tile.LastModified.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void UserDashboardSettings_Constructed_DefaultsLastModifiedToMinValue()
    {
        var settings = new UserDashboardSettings();

        settings.LastModified.Should().Be(DateTime.MinValue);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDashboardEntityDefaultsTests"`

Expected: FAIL — both assertions fail because `LastModified` currently defaults to `DateTime.UtcNow` (a value close to `DateTime.Now`, not `DateTime.MinValue`).

- [ ] **Step 3: Remove the non-zero default from `UserDashboardTile.LastModified`**

In `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs`, change line 11 from:

```csharp
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
```

to:

```csharp
    public DateTime LastModified { get; set; }
```

- [ ] **Step 4: Remove the non-zero default from `UserDashboardSettings.LastModified`**

In `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs`, change line 8 from:

```csharp
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
```

to:

```csharp
    public DateTime LastModified { get; set; }
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserDashboardEntityDefaultsTests"`

Expected: PASS — both `UserDashboardTile_Constructed_DefaultsLastModifiedToMinValue` and `UserDashboardSettings_Constructed_DefaultsLastModifiedToMinValue` pass.

- [ ] **Step 6: Run the full existing Dashboard test suite to confirm no regression**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Dashboard"`

Expected: PASS — all tests in `GetUserSettingsHandlerTests`, `EnableTileHandlerTests`, `DisableTileHandlerTests`, and `SaveUserSettingsHandlerTests` continue to pass unchanged (every construction of `UserDashboardTile`/`UserDashboardSettings` in these files already assigns `LastModified` explicitly, so none of them depended on the old default).

- [ ] **Step 7: Build and format check**

Run: `dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes`

Expected: Build succeeds with no errors; `dotnet format` reports no formatting violations (if it reports violations, run `dotnet format backend/Anela.Heblo.sln` to apply them, then re-verify).

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs \
        backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs \
        backend/test/Anela.Heblo.Tests/Domain/Dashboard/UserDashboardEntityDefaultsTests.cs
git commit -m "fix(dashboard): default LastModified to DateTime.MinValue instead of DateTime.UtcNow

Removes the non-zero property-initializer default on
UserDashboardTile.LastModified and UserDashboardSettings.LastModified,
matching the project's documented TimeProvider-based entity-timestamp
convention. Every production handler already stamps LastModified
explicitly via TimeProvider before persistence; this change only
makes an omitted assignment fail loudly (0001-01-01) instead of
silently recording construction-time wall-clock time.

Fixes #4316"
```

## Self-Review

**Spec coverage:**
- FR-1 (remove `UserDashboardTile.LastModified` default) → Step 3.
- FR-2 (remove `UserDashboardSettings.LastModified` default) → Step 4.
- FR-3 (preserve existing handler behavior, no other call site changes) → verified by Step 6 (no handler/mutator file is touched; full Dashboard suite re-run to confirm).
- NFR-3 (testability strengthened) → verified by the new failing→passing test in Steps 1/2/5, which is exactly the scenario the issue describes (a construction site that omits the explicit assignment now observes an obviously-wrong sentinel).

**Placeholder scan:** No "TBD"/"TODO"/"similar to Task N" placeholders. All code blocks are complete and exact. Commands include expected output.

**Type consistency:** `UserDashboardTile` and `UserDashboardSettings` are used with their exact existing names and no constructor arguments (both project use object-initializer-style construction elsewhere, and both have implicit parameterless constructors since they are plain `Entity<int>` subclasses with only `{ get; set; }` properties) — consistent with every existing construction site referenced in the architecture review (`GetUserSettingsHandlerTests.cs`, `EnableTileHandlerTests.cs`, `DisableTileHandlerTests.cs`, `SaveUserSettingsHandlerTests.cs`).

No gaps found. This is the only task required — the change is a single, atomic, two-file fix plus one new regression test.
