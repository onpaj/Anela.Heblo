# Fix CS1503 in RecurringJobSeeder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the solution build on `main` by passing the per-job `existingConfig` (not the whole `existing` list) to `HasSeededFieldsChanged`.

**Architecture:** One-token change at a single call site in `RecurringJobSeeder.cs`; see `arch-review.r1.md` Decision 1. No new files or tests.

**Tech Stack:** .NET 8, xUnit.

---

### task: fix-seeder-call-site

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51`
- Verify with: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

- [ ] **Step 1: Confirm the failure**

Run: `cd backend && dotnet build src/Anela.Heblo.Application`
Expected: error CS1503 at `RecurringJobSeeder.cs(51,...)`: cannot convert `List<RecurringJobConfiguration>` to `RecurringJobConfiguration`.

- [ ] **Step 2: Apply the fix**

Change line 51 from:

```csharp
            else if (HasSeededFieldsChanged(existing, config))
```

to:

```csharp
            else if (HasSeededFieldsChanged(existingConfig, config))
```

- [ ] **Step 3: Build and format**

Run: `cd backend && dotnet build && dotnet format`
Expected: build succeeds with no CS1503; `dotnet format` produces no diff beyond the fix.

- [ ] **Step 4: Run the seeder tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJobSeederTests"`
Expected: all `RecurringJobSeederTests` pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
git commit -m "fix(background-jobs): pass existingConfig to HasSeededFieldsChanged (CS1503)"
```
