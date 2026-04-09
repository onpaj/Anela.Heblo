# Design: Auto-note on weight outside tolerance confirmation

**Date:** 2026-04-09

## Summary

When a user confirms product completion and the residue weight distribution is outside tolerance, automatically add a note to the manufacturing order recording that the user explicitly approved the out-of-tolerance weight.

## Trigger

`ManufactureOrderApplicationService.ConfirmProductCompletionAsync` is called with `overrideConfirmed = true` **and** `distribution.IsWithinAllowedThreshold == false`.

This happens when:
1. First call returns `NeedsConfirmation` (frontend shows distribution preview modal)
2. User clicks "Potvrdit distribuci"
3. Frontend calls again with `overrideConfirmed: true`

## Change

In `ConfirmProductCompletionAsync`, between the ERP submission step and the `UpdateOrderStatus` call, build a weight tolerance note when the override was confirmed:

```csharp
string? weightToleranceNote = null;
if (overrideConfirmed && !distribution.IsWithinAllowedThreshold)
    weightToleranceNote = $"Hmotnost mimo toleranci potvrzena uživatelem. Rozdíl: {distribution.DifferencePercentage:F2}% (povoleno: {distribution.AllowedResiduePercentage:F2}%)";
```

Combine with the existing ERP error note (if any) before passing to `UpdateOrderStatus`:

```csharp
var noteToSave = string.Join("\n", new[] { orderNote, weightToleranceNote }.Where(n => n != null));
if (string.IsNullOrEmpty(noteToSave))
    noteToSave = $"Potvrzeno dokončení výroby produktů - {submitManufactureResult.ManufactureId}";
```

## Affected files

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`

## Out of scope

- No frontend changes
- No API contract changes
- No new classes or migrations
