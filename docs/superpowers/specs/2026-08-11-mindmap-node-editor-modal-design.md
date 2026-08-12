# Mind map node editor as a modal

**Date:** 2026-08-11
**Status:** Approved, ready for planning
**Area:** `frontend/src/components/pages/automation/mindmaps/`

## Problem

Node editing lives in the "Uzel" tab of the 384px side panel. Notes are the field that
matters most and they get a four-row textarea in a narrow column — unreadable for
anything longer than a sentence. With node editing gone, the side panel holds only
Porady and Historie, neither of which needs to be on screen while working with the map.

## Solution

Move node editing into a modal opened by double-clicking a node. Make the side panel
foldable and fold it by default.

## Scope

| File | Change |
| --- | --- |
| `MindMapNodeEditorDialog.tsx` *(new)* | The modal. Owns `CommitOnBlurField`, `STATUS_LABELS`/`STATUS_OPTIONS`, the lock banner, and the new source-meetings list. |
| `MindMapSidePanel.tsx` | Drops `NodeTab` and the `document` / `selectedNodeId` / `onUpdateNode` props. Keeps Porady + Historie. Gains folding. |
| `MindMapCanvas.tsx` | New `onOpenNodeEditor(nodeId)` prop plus the mind-elixir interception below. |
| `MindMapDetailPage.tsx` | New `editingNodeId` state, renders the modal, wires it. `panelDoc` renamed to `canvasDoc` — it now feeds the modal, not the panel. |
| `MindMapHelpSheet.tsx` | The `dvojklik / F2` row splits in two. |

## Opening the modal on double-click

mind-elixir 5.15.1 does not use a DOM `dblclick` event. It detects double-taps itself
inside its `pointerup` handler and calls `instance.beginEdit(el)`
(`node_modules/mind-elixir/dist/MindElixir.js:1139` — `s.detect(f, d)` at `:1194`
dispatches to `d`, which does `e.selectNode(b), e.beginEdit(b)`). F2 calls the same
method (`:938`). There is therefore no DOM event to `preventDefault` and no
constructor option that disables inline editing.

Three pieces, all inside `MindMapCanvas`'s mount effect:

1. **Patch `instance.beginEdit`.** Save the original (bound to the instance), then
   replace it with a function that resolves the target element (its argument, falling
   back to `instance.currentNode`) and calls `onOpenNodeEditorRef.current(nodeObj.id)`.
   This is the suppression point for the inline `#input-box`.

2. **Restore F2** with a `keydown` listener on `window.document` registered in the
   **capture** phase. On `F2`, when the event target is inside the canvas container and
   the map is not read-only, call `stopPropagation()` and invoke the saved original
   `beginEdit`. Capture-on-document is required, not capture-on-container:
   mind-elixir binds `container.onkeydown` (`:998`) and the container is normally the
   key event's own target, where capture and bubble listeners fire in registration
   order — so a container-level capture listener is not guaranteed to win. The
   target-inside-container guard also makes the listener inert while the user types in
   the modal, which is rendered outside the container.

3. **A `dblclick` listener on the container** that calls `onOpenNodeEditor` for the node
   under the pointer. This covers the read-only map: mind-elixir's own double-tap path
   bails at `if (!e.editable) return` (`:1134`), so the patch in (1) never fires while
   the map is `Updating`. Both paths funnel into the same `setEditingNodeId(id)`, so
   firing both on one double-click is a no-op.

All three are torn down in the mount effect's cleanup; the patch is reverted by
restoring the original method on the instance before `instance.destroy()`.

## The modal

`MindMapNodeEditorDialog` — `max-w-3xl`, `max-h-[85vh]`, fixed overlay with a
`bg-black/40` backdrop, following `MindMapHelpSheet`'s existing structure. Keyed on the
node id so drafts never leak between nodes.

Props: `node: MindMapNode`, `meetings: AttachedMeeting[]`, `isReadOnly: boolean`,
`onUpdateNode(nodeId, patch)`, `onClose()`.

Layout, top to bottom:

- **Název** — full width text input.
- **Poznámky** — full-width textarea, ~10 rows. This is the reason for the redesign.
- **Vlastník** and **Stav** — side by side in a two-column grid.
- **Z porad** — read-only list of `node.sourceMeetingIds` resolved against
  `meetings` by `meetingTranscriptId`, showing subject and `plaudCreatedAt` formatted
  `cs-CZ`. An id with no match renders as *Odpojená porada* (the meeting was detached
  after the node was created). The whole section is omitted when `sourceMeetingIds` is
  empty.
- **Lock banner** — the existing amber "Uzamčeno uživatelem {lockedBy}" notice, shown
  only when `lockedBy` is set.

Behaviour:

- **Commit stays live-on-blur.** `CommitOnBlurField` moves over unchanged: it keeps a
  local draft and calls `onUpdateNode` on blur only when the value actually changed.
  `Stav` is a `<select>` and commits on change, as today. There is no Uložit/Zrušit
  pair; the footer has a single **Zavřít** button.
- **Every close path blurs the active element first.** Escape, backdrop click, the X
  button and Zavřít all call `(document.activeElement as HTMLElement)?.blur()` before
  `onClose()`. Without it, unmounting while a field has focus swallows the edit the
  user just typed — no blur event is dispatched for a removed element.
- **Read-only map** disables every field, exactly as the side panel tab did.
- Escape is handled by a `keydown` listener owned by the dialog.

Related change in `MindMapDetailPage`: the ⌘S handler blurs the active element before
reading `canvasRef.current.getDocument()`, so saving from inside the modal persists the
field currently being typed in. `reshapeNode` mutates `nodeObj` synchronously
(`MindElixir.js:507`), so the flush lands before the read.

`handleOpenNodeEditor(nodeId)` sets `editingNodeId` and pulls a fresh canvas snapshot
into `canvasDoc`, so the modal always opens on current values regardless of which
selection events mind-elixir happened to fire.

## The side panel

- Folded on every mount. State is component-local; nothing is persisted.
- **Folded:** a `w-10` vertical rail with one toggle button,
  `aria-label="Zobrazit panel"`.
- **Expanded:** today's `w-96` panel, with a collapse chevron in the tab bar,
  `aria-label="Skrýt panel"`.
- Tabs reduce to **Porady | Historie**, defaulting to Porady. The effect that switched
  to the Uzel tab on selection is deleted along with the tab.
- The dirty-state guards on attach and restore are untouched.

## Tests

**`MindMapNodeEditorDialog.test.tsx` (new)** — the four node-field tests move here from
`MindMapSidePanel.test.tsx`:

- does not commit on every keystroke in the title field
- commits the title on blur
- does not commit when the text is unchanged
- commits status immediately on change

plus new coverage:

- closing the dialog flushes the field that still has focus
- source meetings render with subject and date
- a `sourceMeetingId` with no matching meeting renders the *Odpojená porada* fallback
- the section is absent when there is no provenance
- the lock banner appears only when `lockedBy` is set
- every field is disabled when `isReadOnly`

**`MindMapSidePanel.test.tsx`** — keeps the attach-while-dirty and restore-while-dirty
guards; loses the node-field tests; gains: starts folded, unfolds on click, refolds.

**`MindMapCanvas.test.tsx`** — gains:

- `beginEdit` calls `onOpenNodeEditor` instead of opening the inline editor
- F2 reaches the original `beginEdit` (inline rename still works)
- a `dblclick` on a topic opens the editor while the map is read-only

**`MindMapDetailPage.test.tsx`** — the "disables the panel and save controls while
Updating" test is repointed at the modal.

## Out of scope

- Persisting the panel's folded state.
- Editing provenance or lock state from the UI; both stay read-only and server-owned.
- A Uložit/Zrušit buffer in the modal — commit-on-blur is deliberate, matching the
  current behaviour and the page-level dirty/save model.
