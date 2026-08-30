### task: add-shoptet-status-comments-to-expedition-picking-request

## Goal

Add the two missing Shoptet-status-name comments to `ExpeditionPickingRequest.cs` so that, once it becomes the sole declaration site (Task 3), none of the domain-meaning documentation carried today only by `PrintPickingListRequest`'s copies is lost (spec FR-3).

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`

**Verify only, no change expected:**
- None for this task.

**Do not touch:**
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — its own copies of these comments are removed in Task 3, not this task; touching it now would leave the comments duplicated for one commit, which is harmless but out of scope for this task's single responsibility.

## Steps

- [ ] **Step 1: Confirm current file content (baseline)**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultSourceStateId\|DefaultDesiredStateId\|DefaultNoteStateId" backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output (no comments yet on the first two lines):
```
7:    public const int DefaultSourceStateId = -2;
8:    public const int DefaultDesiredStateId = 26;
9:    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
12:    public int SourceStateId { get; set; } = DefaultSourceStateId;
13:    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
14:    public int NoteStateId { get; set; } = DefaultNoteStateId;
```

This confirms the two constants that need comments (lines 7-8) and that `DefaultNoteStateId`'s existing comment (line 9) must be left exactly as-is.

- [ ] **Step 2: Add the two Shoptet-status-name comments**

Use the Edit tool on `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs`.

Change:
```csharp
    public const int DefaultSourceStateId = -2;
    public const int DefaultDesiredStateId = 26;
    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
```

to:
```csharp
    public const int DefaultSourceStateId = -2; // Vyrizuje se
    public const int DefaultDesiredStateId = 26; // Bali se
    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
```

The comment text (`Vyrizuje se`, `Bali se`) is copied verbatim from `PrintPickingListRequest.cs`'s existing comments (line 7 and line 9 there) — this is the domain documentation being preserved, not new wording. Do not change the `DefaultNoteStateId` line.

- [ ] **Step 3: Build to confirm the comment-only change compiles**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && dotnet build Anela.Heblo.sln
```

Expected: build succeeds with no new errors or warnings. Comments have zero effect on compiled output, so this should be unconditionally green.

- [ ] **Step 4: Verify the comments landed correctly**

Run:
```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && grep -n "DefaultSourceStateId\|DefaultDesiredStateId\|DefaultNoteStateId" backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs
```

Expected output:
```
7:    public const int DefaultSourceStateId = -2; // Vyrizuje se
8:    public const int DefaultDesiredStateId = 26; // Bali se
9:    public const int DefaultNoteStateId = 35; // Poznámka — orders with incomplete address
12:    public int SourceStateId { get; set; } = DefaultSourceStateId;
13:    public int DesiredStateId { get; set; } = DefaultDesiredStateId;
14:    public int NoteStateId { get; set; } = DefaultNoteStateId;
```

- [ ] **Step 5: Commit**

```bash
cd /home/user/worktrees/feature-3987-Arch-Review-Expeditionlist-Shoptet-State-Id-Consta && git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs && git commit -m "$(cat <<'EOF'
docs(expedition-list): add Shoptet status-name comments to ExpeditionPickingRequest

Prepares ExpeditionPickingRequest to become the sole declaration site for
the three Shoptet order-status ID constants (next commits) without losing
the "Vyrizuje se" / "Bali se" documentation currently only present on
PrintPickingListRequest's soon-to-be-removed duplicate constants.

No behavior change.
EOF
)"
```

---
