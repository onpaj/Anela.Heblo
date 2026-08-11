# Mind Map → mind-elixir Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hand-written React Flow mind map canvas with the `mind-elixir` library, so drag-to-reparent, PNG/SVG export, multi-select and copy/paste come from a maintained upstream instead of code we own.

**Architecture:** The backend contract (`MindMapDocument`: a flat `nodes[]` array with `id`/`parentId`) is unchanged — no migration, no guard change, no API change. A new pure mapping layer converts that flat document to and from mind-elixir's nested `NodeObj` tree, carrying our extra fields (`status`, `owner`, `lockedBy`, `sourceMeetingIds`) in mind-elixir's generic `metadata` slot. The mind-elixir instance becomes the owner of live editing state; React keeps only the server document, a dirty flag, the current selection, and a read-only snapshot for the side panel.

**Tech Stack:** React 18, TypeScript, react-scripts 5.0.1 (CRA), Jest + Testing Library, Playwright (e2e), `mind-elixir` ^5.15.1.

## Global Constraints

- **Pin mind-elixir to `^5.15.1`.** npm's `latest` dist-tag currently points at `6.0.0-next.4`, a prerelease. A bare `npm install mind-elixir` installs the prerelease. Always install with an explicit version.
- **Install with `--legacy-peer-deps`.** This repo's CI uses `npm install --legacy-peer-deps` (`.github/workflows/ci-feature-branch.yml:41`); a plain install fails with ERESOLVE.
- **mind-elixir is ESM-only.** Its `exports` map has `import` only, no `require`. It must be added to the Jest `transformIgnorePatterns` allowlist in `frontend/package.json`.
- **No backend changes.** Do not touch anything under `backend/`. `MindMapDocument.cs`, `MindMapGuard.cs`, `MindMapLockService.cs` and the migrations stay exactly as they are.
- **Node ids are server-owned.** `MindMapLockService.ApplyUserEdit` diffs by id: an id it does not recognise is treated as a brand-new node (fresh Guid, fresh lock, empty provenance). Existing ids must therefore survive the round trip byte-identical. Ids that mind-elixir generates for new nodes are fine — they are *meant* to be seen as new.
- **Sibling order is array order** in `MindMapDocument.nodes`, and it round-trips through both the save path and the LLM guard. The mapper must preserve it in both directions.
- **All UI copy is Czech**, matching the existing components.
- **Verification gate before any task is considered done:** `CI=false npm run build` and `CI=true npx react-scripts test --watchAll=false` from `frontend/`, both clean. Lint must not gain new errors (baseline is 177 pre-existing errors: `npm run lint 2>&1 | grep -c error`).

## Verified API facts

These were checked against the published type definitions of `mind-elixir@5.15.1`. Do not re-derive them.

| Fact | Value |
|---|---|
| Construction | `new MindElixir(options)` then `instance.init(data)` |
| Two-sided layout | `direction: MindElixir.SIDE` (`SIDE = 2`; `LEFT = 0`, `RIGHT = 1`, `DOWN = 3`) |
| Built-in themes | `MindElixir.THEME`, `MindElixir.DARK_THEME` |
| Node shape | `NodeObj<M>` = `{ id, topic, children?, note?, tags?, icons?, expanded?, direction?, branchColor?, style?, metadata?: M }` |
| Node `style` keys | only `fontSize`, `fontFamily`, `color`, `background`, `fontWeight`, `width`, `border`, `textDecoration` |
| Data envelope | `MindElixirData` = `{ nodeData: NodeObj, arrows?, summaries?, direction?, theme?, meta? }` |
| Events | `instance.bus.addListener(type, handler)`; types include `operation`, `selectNewNode`, `selectNodes`, `unselectNodes`, `expandNode`, `scale`, `move` |
| Edit API | `addChild`, `insertSibling('before'\|'after')`, `removeNodes`, `moveUpNode`, `moveDownNode`, `moveNodeIn/Before/After`, `reshapeNode(el, patch)`, `setNodeTopic`, `beginEdit` |
| Read-only | `disableEdit()` / `enableEdit()` |
| Undo | `undo()`, `redo()`, `clearHistory()` (call after `refresh()`) |
| Sync | `getData()`, `getDataString()`, `refresh(data)` |
| Viewport | `scaleFit()`, `toCenter()`, `scale(v)` |
| Expand | `expandNode(el, isExpand?)`, `expandNodeAll(el, isExpand?)` |
| Export | `exportPng(noForeignObject?, injectCss?) => Promise<Blob \| null>`, `exportSvg(...) => Blob` |
| Element lookup | `instance.findEle(id)` returns the `Topic` element |
| Theming | `Theme` = `{ name, type?, palette: string[], cssVar?, generateMainBranch?, generateSubBranch? }`; `changeTheme(theme, shouldRefresh?)` |
| Veto hooks | `before: { [operation]: (...args) => boolean \| Promise<boolean> }` |
| Rendered DOM | topics are `me-tpc` elements carrying `data-nodeid`; wrappers `me-root` / `me-main` / `me-wrapper` / `me-parent` / `me-children`; expander `me-epd`; container `.map-container` |
| CSS import | `import "mind-elixir/style"` |

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `frontend/src/components/pages/automation/mindmaps/mindElixirMapping.ts` | Pure `MindMapDocument` ⇄ `MindElixirData` conversion, plus the derived display fields (tags/icons/style) computed from our metadata |
| `frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts` | Our palette expressed as mind-elixir light/dark `Theme` objects |
| `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirMapping.test.ts` | Round-trip and field-fidelity tests |
| `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts` | Theme shape tests |

**Modified**

| File | Change |
|---|---|
| `frontend/package.json` | add `mind-elixir`, drop `@xyflow/react`, extend `transformIgnorePatterns` |
| `.../mindmaps/MindMapCanvas.tsx` | rewritten as an imperative mind-elixir host exposing a ref handle |
| `.../mindmaps/MindMapDetailPage.tsx` | state ownership flips to the instance; save/adopt/dirty rewired |
| `.../mindmaps/MindMapSidePanel.tsx` | field edits route through the canvas handle; text inputs become locally controlled |
| `.../mindmaps/MindMapToolbar.tsx` | actions call the canvas handle; export buttons added |
| `.../mindmaps/MindMapHelpSheet.tsx` | shortcut table rewritten to mind-elixir's real bindings |
| `.../mindmaps/mindMapTheme.ts` | reduced to the palette only |
| `.../mindmaps/__tests__/MindMapCanvas.test.tsx` | rewritten against a mocked mind-elixir |
| `.../mindmaps/__tests__/MindMapDetailPage.test.tsx` | canvas mock updated to the new prop/ref surface |
| `frontend/test/e2e/mindmaps/mindmap.spec.ts` | selectors moved to `me-tpc` / `[data-nodeid]` |

**Deleted**

`mindMapLayout.ts`, `mindMapTextMeasure.ts`, `mindMapFlow.ts`, `mindMapInteraction.ts`, `MindMapCurvedEdge.tsx`, `MindMapFlowNode.tsx`, `useMindMapKeyboard.ts`, `useMindMapUndo.ts`, and the tests `mindMapLayout.test.ts`, `mindMapFlow.test.ts`, `useMindMapKeyboard.test.tsx`.

`mindMapDocument.ts` **stays** — `parseDocument`, `visibleNodeIds` and the tree-editing helpers are still used by tests and the mapper. Its structural editors (`indentNode`, `outdentNode`, `moveNode`, `addSiblingNode`, `addChildNode`, `setAllCollapsed`) become unused once the canvas owns editing; Task 8 removes exactly the ones with no remaining callers.

---

### Task 1: Dependency and toolchain plumbing

**Files:**
- Modify: `frontend/package.json`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirSmoke.test.ts` (created, deleted at the end of this task)

**Interfaces:**
- Consumes: nothing
- Produces: `mind-elixir` importable from application code and parseable by Jest.

- [ ] **Step 1: Install the pinned version**

```bash
cd frontend
npm install mind-elixir@^5.15.1 --legacy-peer-deps
```

Confirm `package.json` shows `"mind-elixir": "^5.15.1"` and NOT a `6.0.0-next` version. If it shows a prerelease, the pin was ignored — fix it by hand and re-run `npm install --legacy-peer-deps`.

- [ ] **Step 2: Write a failing smoke test**

Create `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirSmoke.test.ts`:

```ts
import MindElixir from "mind-elixir";

test("mind-elixir is importable and exposes the constants the migration relies on", () => {
  expect(MindElixir.SIDE).toBe(2);
  expect(typeof MindElixir.new).toBe("function");
  expect(MindElixir.THEME).toEqual(expect.objectContaining({ palette: expect.any(Array) }));
  expect(MindElixir.DARK_THEME).toEqual(expect.objectContaining({ palette: expect.any(Array) }));
});
```

- [ ] **Step 3: Run it and watch it fail on the ESM parse**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirSmoke" --watchAll=false`

Expected: FAIL with `SyntaxError: Cannot use import statement outside a module`. This is the ESM-only constraint from Global Constraints, and it is the whole point of this task.

- [ ] **Step 4: Add mind-elixir to the Jest transform allowlist**

In `frontend/package.json`, change:

```json
  "jest": {
    "transformIgnorePatterns": [
      "node_modules/(?!(date-fns)/)"
    ]
  }
```

to:

```json
  "jest": {
    "transformIgnorePatterns": [
      "node_modules/(?!(date-fns|mind-elixir)/)"
    ]
  }
```

- [ ] **Step 5: Run the smoke test again**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirSmoke" --watchAll=false`

Expected: PASS.

- [ ] **Step 6: Delete the smoke test**

It has served its purpose — it proved the toolchain accepts the package. Keeping it would tie the suite to upstream constants that later tasks assert properly.

```bash
rm frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirSmoke.test.ts
```

- [ ] **Step 7: Verify the whole suite and build still pass**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false && CI=false npm run build`

Expected: all suites pass, `Compiled successfully.` Widening `transformIgnorePatterns` can surface unrelated failures; if it does, stop and report rather than pressing on.

- [ ] **Step 8: Commit**

```bash
git add frontend/package.json frontend/package-lock.json
git commit -m "chore: add mind-elixir and allow it through the jest transform"
```

---

### Task 2: Document ⇄ mind-elixir mapping layer

This is the highest-risk part of the migration and the reason it gets full unit coverage: every field that fails to round-trip is silent data loss on the next save.

**Files:**
- Create: `frontend/src/components/pages/automation/mindmaps/mindElixirMapping.ts`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirMapping.test.ts`

**Interfaces:**
- Consumes: `MindMapDocument`, `MindMapNode`, `MindMapNodeStatus` from `./mindMapDocument`
- Produces:
  - `interface MindMapNodeMetadata { status: MindMapNodeStatus; owner: string | null; lockedBy: string | null; sourceMeetingIds: string[] }`
  - `type MindMapNodeObj = NodeObj<MindMapNodeMetadata>`
  - `toMindElixir(doc: MindMapDocument): MindElixirData`
  - `fromMindElixir(data: MindElixirData, previous: MindMapDocument): MindMapDocument`
  - `displayFieldsFor(metadata: MindMapNodeMetadata, notes: string | null): Pick<MindMapNodeObj, "tags" | "icons" | "style">`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirMapping.test.ts`:

```ts
import { MindMapDocument, MindMapNode } from "../mindMapDocument";
import { displayFieldsFor, fromMindElixir, toMindElixir } from "../mindElixirMapping";

function node(id: string, parentId: string | null, overrides: Partial<MindMapNode> = {}): MindMapNode {
  return {
    id,
    parentId,
    title: id,
    notes: null,
    status: "active",
    owner: null,
    lockedBy: null,
    sourceMeetingIds: [],
    position: null,
    collapsed: false,
    ...overrides,
  };
}

const doc = (): MindMapDocument => ({
  schemaVersion: 1,
  rootNodeId: "root",
  nodes: [
    node("root", null, { title: "Anela\notevřená témata" }),
    node("a", "root", { title: "Cílovky", owner: "Bára", status: "idea" }),
    node("b", "root", { title: "Parkoviště", collapsed: true, lockedBy: "ondra@anela.cz" }),
    node("a1", "a", { title: "35–45", notes: "delší poznámka", sourceMeetingIds: ["m1", "m2"] }),
    node("a2", "a", { title: "Precedens", status: "done" }),
  ],
  suppressedNodes: [{ title: "Smazané téma", deletedBy: "ondra@anela.cz" }],
});

test("toMindElixir nests children under their parent in document order", () => {
  const data = toMindElixir(doc());
  expect(data.nodeData.id).toBe("root");
  expect(data.nodeData.children?.map((c) => c.id)).toEqual(["a", "b"]);
  const a = data.nodeData.children![0];
  expect(a.children?.map((c) => c.id)).toEqual(["a1", "a2"]);
});

test("toMindElixir maps title, notes and collapsed onto topic, note and expanded", () => {
  const data = toMindElixir(doc());
  expect(data.nodeData.topic).toBe("Anela\notevřená témata");
  const [a, b] = data.nodeData.children!;
  expect(a.expanded).toBe(true);
  expect(b.expanded).toBe(false);
  expect(a.children![0].note).toBe("delší poznámka");
});

test("toMindElixir carries our extra fields in metadata", () => {
  const data = toMindElixir(doc());
  const a = data.nodeData.children![0];
  expect(a.metadata).toEqual({
    status: "idea",
    owner: "Bára",
    lockedBy: null,
    sourceMeetingIds: [],
  });
  expect(a.children![0].metadata?.sourceMeetingIds).toEqual(["m1", "m2"]);
});

// A round trip returns the flat array in depth-first order (a parent immediately
// followed by its subtree) rather than the order it went in. That is a reordering
// of the array, not a loss: what carries meaning is each parent's sibling order,
// and nothing — not the layout, not MindMapGuard, not MindMapLockService, all of
// which key by id — reads the absolute index. Compare accordingly.
const nodesById = (d: MindMapDocument) => Object.fromEntries(d.nodes.map((n) => [n.id, n]));
const siblingOrder = (d: MindMapDocument, parentId: string | null) =>
  d.nodes.filter((n) => n.parentId === parentId).map((n) => n.id);

test("a document round-trips through mind-elixir without losing a field", () => {
  const original = doc();
  const restored = fromMindElixir(toMindElixir(original), original);
  expect(restored.nodes).toHaveLength(original.nodes.length);
  expect(nodesById(restored)).toEqual(nodesById(original));
  expect(restored.schemaVersion).toBe(original.schemaVersion);
  expect(restored.rootNodeId).toBe(original.rootNodeId);
  expect(restored.suppressedNodes).toEqual(original.suppressedNodes);
});

test("round-trip preserves every parent's sibling order", () => {
  const original = doc();
  const restored = fromMindElixir(toMindElixir(original), original);
  expect(siblingOrder(restored, null)).toEqual(siblingOrder(original, null));
  expect(siblingOrder(restored, "root")).toEqual(siblingOrder(original, "root"));
  expect(siblingOrder(restored, "a")).toEqual(siblingOrder(original, "a"));
});

test("fromMindElixir defaults a node mind-elixir created itself", () => {
  const data = toMindElixir(doc());
  // Mimic mind-elixir's addChild: a node with an id and topic and nothing else.
  data.nodeData.children!.push({ id: "me-generated", topic: "Nový uzel" });
  const restored = fromMindElixir(data, doc());
  const added = restored.nodes.find((n) => n.id === "me-generated")!;
  expect(added).toEqual(
    expect.objectContaining({
      parentId: "root",
      title: "Nový uzel",
      status: "active",
      owner: null,
      lockedBy: null,
      sourceMeetingIds: [],
      collapsed: false,
    }),
  );
});

test("fromMindElixir carries suppressedNodes and schemaVersion from the previous document", () => {
  // The library knows nothing about tombstones; they must survive every save.
  const restored = fromMindElixir(toMindElixir(doc()), doc());
  expect(restored.suppressedNodes).toEqual([{ title: "Smazané téma", deletedBy: "ondra@anela.cz" }]);
  expect(restored.schemaVersion).toBe(1);
});

test("toMindElixir throws when the root id is missing rather than emitting a headless map", () => {
  const broken: MindMapDocument = { ...doc(), rootNodeId: "nope" };
  expect(() => toMindElixir(broken)).toThrow(/root/i);
});

test("toMindElixir terminates when the root lists itself as its own parent", () => {
  // Each node has exactly one parentId, so the only cycle the walk can actually
  // reach from the root is a self-parenting node: childrenByParent["root"] then
  // contains root itself, and an unguarded build() would recurse forever.
  const cyclic: MindMapDocument = {
    schemaVersion: 1,
    rootNodeId: "root",
    nodes: [node("root", "root"), node("a", "root")],
    suppressedNodes: [],
  };
  const data = toMindElixir(cyclic);
  expect(data.nodeData.id).toBe("root");
  expect(data.nodeData.children?.map((c) => c.id)).toEqual(["a"]);
}, 2000);

test("displayFieldsFor renders the owner as a tag and the lock and note as icons", () => {
  const fields = displayFieldsFor(
    { status: "active", owner: "Bára", lockedBy: "ondra@anela.cz", sourceMeetingIds: [] },
    "poznámka",
  );
  expect(fields.tags).toEqual(["Bára"]);
  expect(fields.icons).toEqual(["🔒", "📝"]);
});

test("displayFieldsFor styles idea, done and blocked distinctly", () => {
  const base = { owner: null, lockedBy: null, sourceMeetingIds: [] };
  expect(displayFieldsFor({ ...base, status: "idea" }, null).style).toEqual(
    expect.objectContaining({ border: expect.stringContaining("dashed") }),
  );
  expect(displayFieldsFor({ ...base, status: "done" }, null).style).toEqual(
    expect.objectContaining({ textDecoration: "line-through" }),
  );
  expect(displayFieldsFor({ ...base, status: "blocked" }, null).style).toEqual(
    expect.objectContaining({ border: expect.stringContaining("#EF4444") }),
  );
  expect(displayFieldsFor({ ...base, status: "active" }, null).style).toBeUndefined();
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirMapping" --watchAll=false`

Expected: FAIL with `Cannot find module '../mindElixirMapping'`.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/components/pages/automation/mindmaps/mindElixirMapping.ts`:

```ts
// Conversion between our persisted MindMapDocument (flat nodes[] with parentId —
// the shape the backend validates, guards and diffs by id) and mind-elixir's
// nested NodeObj tree. Pure: no DOM, no library instance.
//
// Our extra fields ride in mind-elixir's generic `metadata` slot. `tags`, `icons`
// and `style` are DERIVED display fields — never a source of truth. They are
// recomputed by displayFieldsFor() here and again on every reshapeNode(), so the
// two can never disagree.

import type { MindElixirData, NodeObj } from "mind-elixir";
import { MindMapDocument, MindMapNode, MindMapNodeStatus } from "./mindMapDocument";

export interface MindMapNodeMetadata {
  status: MindMapNodeStatus;
  owner: string | null;
  lockedBy: string | null;
  sourceMeetingIds: string[];
}

export type MindMapNodeObj = NodeObj<MindMapNodeMetadata>;

const LOCK_ICON = "🔒";
const NOTE_ICON = "📝";
const IDEA_BORDER = "1px dashed #8A827B";
const BLOCKED_BORDER = "1px solid #EF4444";

export const DEFAULT_METADATA: MindMapNodeMetadata = {
  status: "active",
  owner: null,
  lockedBy: null,
  sourceMeetingIds: [],
};

export function displayFieldsFor(
  metadata: MindMapNodeMetadata,
  notes: string | null,
): Pick<MindMapNodeObj, "tags" | "icons" | "style"> {
  const icons = [
    ...(metadata.lockedBy ? [LOCK_ICON] : []),
    ...(notes ? [NOTE_ICON] : []),
  ];

  let style: MindMapNodeObj["style"];
  if (metadata.status === "idea") style = { border: IDEA_BORDER, color: "#8A827B" };
  else if (metadata.status === "done") style = { textDecoration: "line-through" };
  else if (metadata.status === "blocked") style = { border: BLOCKED_BORDER };

  return {
    tags: metadata.owner ? [metadata.owner] : undefined,
    icons: icons.length > 0 ? icons : undefined,
    style,
  };
}

export function toMindElixir(doc: MindMapDocument): MindElixirData {
  const root = doc.nodes.find((n) => n.id === doc.rootNodeId);
  if (!root) throw new Error(`Mind map document has no node for its root id '${doc.rootNodeId}'.`);

  const childrenByParent = new Map<string, MindMapNode[]>();
  for (const node of doc.nodes) {
    if (!node.parentId) continue;
    const siblings = childrenByParent.get(node.parentId);
    // Document array order IS sibling order — preserve it.
    childrenByParent.set(node.parentId, siblings ? [...siblings, node] : [node]);
  }

  const seen = new Set<string>(); // cycle guard: a malformed parentId chain must not hang the tab
  const build = (node: MindMapNode): MindMapNodeObj => {
    seen.add(node.id);
    const metadata: MindMapNodeMetadata = {
      status: node.status,
      owner: node.owner,
      lockedBy: node.lockedBy,
      sourceMeetingIds: [...node.sourceMeetingIds],
    };
    const children = (childrenByParent.get(node.id) ?? [])
      .filter((child) => !seen.has(child.id))
      .map(build);

    return {
      id: node.id,
      topic: node.title,
      note: node.notes ?? undefined,
      expanded: !node.collapsed,
      children: children.length > 0 ? children : undefined,
      metadata,
      ...displayFieldsFor(metadata, node.notes),
    };
  };

  return { nodeData: build(root) };
}

export function fromMindElixir(data: MindElixirData, previous: MindMapDocument): MindMapDocument {
  const nodes: MindMapNode[] = [];

  const walk = (obj: MindMapNodeObj, parentId: string | null): void => {
    const metadata = obj.metadata ?? DEFAULT_METADATA;
    nodes.push({
      id: obj.id,
      parentId,
      title: obj.topic,
      notes: obj.note ?? null,
      status: metadata.status ?? "active",
      owner: metadata.owner ?? null,
      lockedBy: metadata.lockedBy ?? null,
      sourceMeetingIds: metadata.sourceMeetingIds ? [...metadata.sourceMeetingIds] : [],
      // The redesign dropped manual positioning; the layout is always computed.
      position: null,
      collapsed: obj.expanded === false,
    });
    for (const child of obj.children ?? []) {
      walk(child as MindMapNodeObj, obj.id);
    }
  };
  walk(data.nodeData as MindMapNodeObj, null);

  return {
    // The library has no concept of tombstones or our schema version; both are
    // carried forward from the document the editor was loaded with.
    schemaVersion: previous.schemaVersion,
    rootNodeId: data.nodeData.id,
    nodes,
    suppressedNodes: previous.suppressedNodes,
  };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirMapping" --watchAll=false`

Expected: PASS, 11 tests.

If the round-trip test fails on `position`, note that the fixture builds every node with `position: null` and the mapper always emits `null` — a failure there means a node in the fixture was given a non-null position, which the mapper deliberately discards. Do not "fix" the mapper to carry positions; fix the fixture.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/mindElixirMapping.ts \
        frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirMapping.test.ts
git commit -m "feat: add MindMapDocument <-> mind-elixir mapping layer"
```

---

### Task 3: Theme

**Files:**
- Create: `frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts`
- Modify: `frontend/src/components/pages/automation/mindmaps/mindMapTheme.ts`

**Interfaces:**
- Consumes: `MIND_MAP_PALETTE` from `./mindMapTheme`
- Produces: `MIND_MAP_LIGHT_THEME: Theme`, `MIND_MAP_DARK_THEME: Theme`, `themeFor(mode: "light" | "dark"): Theme`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts`:

```ts
import { MIND_MAP_PALETTE } from "../mindMapTheme";
import { MIND_MAP_DARK_THEME, MIND_MAP_LIGHT_THEME, themeFor } from "../mindElixirTheme";

test("both themes expose our branch palette", () => {
  expect(MIND_MAP_LIGHT_THEME.palette).toEqual([...MIND_MAP_PALETTE]);
  expect(MIND_MAP_DARK_THEME.palette).toEqual([...MIND_MAP_PALETTE]);
});

test("themes are tagged so mind-elixir picks matching built-in styling", () => {
  expect(MIND_MAP_LIGHT_THEME.type).toBe("light");
  expect(MIND_MAP_DARK_THEME.type).toBe("dark");
});

test("themes have distinct names so changeTheme() actually re-renders", () => {
  // mind-elixir skips a theme change when the name is unchanged.
  expect(MIND_MAP_LIGHT_THEME.name).not.toBe(MIND_MAP_DARK_THEME.name);
});

test("the light theme keeps the template's warm paper background and ink root", () => {
  expect(MIND_MAP_LIGHT_THEME.cssVar?.["--root-bgcolor"]).toBe("#2B2724");
  expect(MIND_MAP_LIGHT_THEME.cssVar?.["--root-color"]).toBe("#FFFFFF");
});

test("themeFor selects by mode", () => {
  expect(themeFor("light")).toBe(MIND_MAP_LIGHT_THEME);
  expect(themeFor("dark")).toBe(MIND_MAP_DARK_THEME);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirTheme" --watchAll=false`

Expected: FAIL with `Cannot find module '../mindElixirTheme'`.

- [ ] **Step 3: Write the implementation**

Create `frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts`:

```ts
// Our branch palette and card colours expressed as mind-elixir themes. The values
// come from the "Anela — otevřená témata" template: warm paper ground, near-black
// inverted root in light mode, near-white inverted root in dark mode.

import type { Theme } from "mind-elixir";
import { MIND_MAP_PALETTE } from "./mindMapTheme";

export const MIND_MAP_LIGHT_THEME: Theme = {
  name: "anela-light",
  type: "light",
  palette: [...MIND_MAP_PALETTE],
  cssVar: {
    "--node-gap-x": "12px",
    "--node-gap-y": "12px",
    "--main-gap-x": "36px",
    "--main-gap-y": "12px",
    "--main-color": "#2B2724",
    "--main-bgcolor": "#FFFFFF",
    "--color": "#2B2724",
    "--bgcolor": "#FAF8F5",
    "--selected": "#1F6FB2",
    "--root-color": "#FFFFFF",
    "--root-bgcolor": "#2B2724",
    "--root-border-color": "#2B2724",
    "--root-radius": "14px",
    "--main-radius": "9px",
    "--topic-padding": "7px 13px",
  },
};

export const MIND_MAP_DARK_THEME: Theme = {
  name: "anela-dark",
  type: "dark",
  palette: [...MIND_MAP_PALETTE],
  cssVar: {
    "--node-gap-x": "12px",
    "--node-gap-y": "12px",
    "--main-gap-x": "36px",
    "--main-gap-y": "12px",
    "--main-color": "#E6E3DF",
    "--main-bgcolor": "#1B1C1F",
    "--color": "#E6E3DF",
    "--bgcolor": "#141517",
    "--selected": "#4FA3E3",
    "--root-color": "#2B2724",
    "--root-bgcolor": "#EDE7DF",
    "--root-border-color": "#EDE7DF",
    "--root-radius": "14px",
    "--main-radius": "9px",
    "--topic-padding": "7px 13px",
  },
};

export function themeFor(mode: "light" | "dark"): Theme {
  return mode === "dark" ? MIND_MAP_DARK_THEME : MIND_MAP_LIGHT_THEME;
}
```

- [ ] **Step 4: Leave `mindMapTheme.ts` alone**

Do **not** strip the layout constants, tier metrics or font strings from `mindMapTheme.ts` in this task, even though the mind-elixir themes only need `MIND_MAP_PALETTE`. `mindMapLayout.ts`, `mindMapFlow.ts` and `MindMapFlowNode.tsx` still import those exports and are not deleted until Task 8; removing them here would leave the build broken across Tasks 3–7, and every task must satisfy the verification gate in Global Constraints on its own. Task 8 Step 3 reduces this file once its last consumer is gone.

- [ ] **Step 5: Run the full gate**

Run:

```bash
cd frontend
CI=true npx react-scripts test --watchAll=false
CI=false npm run build
```

Expected: all suites pass and `Compiled successfully.` — this task is purely additive, so nothing existing may break.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts \
        frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts
git commit -m "feat: express the mind map palette as mind-elixir light/dark themes"
```

---

### Task 4: MindMapCanvas as a mind-elixir host

**Files:**
- Modify (full rewrite): `frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx`
- Test (full rewrite): `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx`

**Interfaces:**
- Consumes: `toMindElixir`, `fromMindElixir`, `displayFieldsFor`, `MindMapNodeMetadata`, `DEFAULT_METADATA` (Task 2); `themeFor` (Task 3)
- Produces:
  - `interface MindMapCanvasHandle { getDocument(): MindMapDocument | null; expandAll(): void; collapseAll(): void; fit(): void; addChild(): void; addSibling(): void; undo(): void; patchNode(nodeId: string, patch: MindMapNodePatch): void; exportPng(): Promise<Blob | null>; exportSvg(): Blob | null; }`

  Note there is deliberately no `remove()`: deletion is reached through ⌫, and an unused handle method is dead code.
  - `type MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>`
  - `interface MindMapCanvasProps { initialDocument, documentRevision, isReadOnly, onChange, onSelectNode }`

- [ ] **Step 1: Write the failing test**

Replace `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx` entirely:

```tsx
import React from "react";
import { act, render } from "@testing-library/react";
import "@testing-library/jest-dom";
import { MindMapDocument } from "../mindMapDocument";

jest.mock("mind-elixir/style", () => ({}));

// mind-elixir drives real DOM layout (offsetWidth, getBoundingClientRect), all of
// which jsdom reports as 0 — a real instance renders an empty, meaningless map.
// Stub the class so these tests exercise MindMapCanvas's own wiring: mount once,
// refresh on revision change, toggle edit mode, translate bus events outward.
const instance = {
  init: jest.fn(),
  destroy: jest.fn(),
  refresh: jest.fn(),
  clearHistory: jest.fn(),
  getData: jest.fn(),
  enableEdit: jest.fn(),
  disableEdit: jest.fn(),
  undo: jest.fn(),
  scaleFit: jest.fn(),
  toCenter: jest.fn(),
  addChild: jest.fn(),
  insertSibling: jest.fn(),
  removeNodes: jest.fn(),
  reshapeNode: jest.fn(),
  expandNode: jest.fn(),
  expandNodeAll: jest.fn(),
  findEle: jest.fn(() => ({ nodeObj: { id: "root" } })),
  changeTheme: jest.fn(),
  exportPng: jest.fn(),
  exportSvg: jest.fn(),
  nodeData: { id: "root" },
  currentNode: null as unknown,
  bus: {
    listeners: {} as Record<string, Function[]>,
    addListener(type: string, handler: Function) {
      (this.listeners[type] ??= []).push(handler);
    },
    removeListener(type: string, handler: Function) {
      this.listeners[type] = (this.listeners[type] ?? []).filter((h) => h !== handler);
    },
    fire(type: string, ...args: unknown[]) {
      (this.listeners[type] ?? []).forEach((h) => h(...args));
    },
  },
};

const MindElixirMock: any = jest.fn(() => instance);
MindElixirMock.SIDE = 2;
MindElixirMock.LEFT = 0;
MindElixirMock.RIGHT = 1;

jest.mock("mind-elixir", () => ({ __esModule: true, default: MindElixirMock }));

// eslint-disable-next-line import/first
import MindMapCanvas, { MindMapCanvasHandle } from "../MindMapCanvas";

function buildDoc(): MindMapDocument {
  return {
    schemaVersion: 1,
    rootNodeId: "root",
    nodes: [
      {
        id: "root",
        parentId: null,
        title: "Projekt",
        notes: null,
        status: "active",
        owner: null,
        lockedBy: null,
        sourceMeetingIds: [],
        position: null,
        collapsed: false,
      },
    ],
    suppressedNodes: [],
  };
}

function renderCanvas(overrides: Partial<React.ComponentProps<typeof MindMapCanvas>> = {}) {
  const ref = React.createRef<MindMapCanvasHandle>();
  const utils = render(
    <MindMapCanvas
      ref={ref}
      initialDocument={buildDoc()}
      documentRevision="rev-1"
      isReadOnly={false}
      onChange={jest.fn()}
      onSelectNode={jest.fn()}
      {...overrides}
    />,
  );
  return { ref, ...utils };
}

describe("MindMapCanvas", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    instance.bus.listeners = {};
  });

  it("creates the instance with the two-sided layout and initialises it once", () => {
    renderCanvas();
    expect(MindElixirMock).toHaveBeenCalledTimes(1);
    expect(MindElixirMock.mock.calls[0][0]).toEqual(
      expect.objectContaining({ direction: 2, allowUndo: true }),
    );
    expect(instance.init).toHaveBeenCalledTimes(1);
  });

  it("keeps mind-elixir's own context menu and toolbar off", () => {
    // The context menu can create arrows and summaries, neither of which
    // MindMapDocument stores — they would be silently dropped on the next save —
    // and mind-elixir has no Czech language pack for it.
    renderCanvas();
    expect(MindElixirMock.mock.calls[0][0]).toEqual(
      expect.objectContaining({ contextMenu: false, toolBar: false }),
    );
  });

  it("does not re-initialise when the revision is unchanged", () => {
    const { rerender, ref } = renderCanvas();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-1"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.refresh).not.toHaveBeenCalled();
  });

  it("refreshes and clears history when a new server revision arrives", () => {
    // clearHistory matters: without it ⌘Z can undo backwards into the previous
    // document, producing node ids the server has already replaced.
    const { rerender, ref } = renderCanvas();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-2"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.refresh).toHaveBeenCalledTimes(1);
    expect(instance.clearHistory).toHaveBeenCalledTimes(1);
  });

  it("reports edits upward so the page can mark the document dirty", () => {
    const onChange = jest.fn();
    renderCanvas({ onChange });
    act(() => {
      instance.bus.fire("operation", { name: "addChild", obj: { id: "x" } });
    });
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("treats collapsing a branch as an edit — `collapsed` is a persisted field", () => {
    const onChange = jest.fn();
    renderCanvas({ onChange });
    act(() => {
      instance.bus.fire("expandNode", { id: "x" });
    });
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it("reports selection and deselection upward", () => {
    const onSelectNode = jest.fn();
    renderCanvas({ onSelectNode });
    act(() => {
      instance.bus.fire("selectNewNode", { id: "a" });
    });
    expect(onSelectNode).toHaveBeenCalledWith("a");
    act(() => {
      instance.bus.fire("unselectNodes", [{ id: "a" }]);
    });
    expect(onSelectNode).toHaveBeenCalledWith(null);
  });

  it("disables editing while the map is read-only and re-enables it after", () => {
    const { rerender, ref } = renderCanvas({ isReadOnly: true });
    expect(instance.disableEdit).toHaveBeenCalled();
    rerender(
      <MindMapCanvas
        ref={ref}
        initialDocument={buildDoc()}
        documentRevision="rev-1"
        isReadOnly={false}
        onChange={jest.fn()}
        onSelectNode={jest.fn()}
      />,
    );
    expect(instance.enableEdit).toHaveBeenCalled();
  });

  it("exposes the current document through the ref handle", () => {
    instance.getData.mockReturnValue({ nodeData: { id: "root", topic: "Projekt" } });
    const { ref } = renderCanvas();
    expect(ref.current!.getDocument()).toEqual(
      expect.objectContaining({ rootNodeId: "root", schemaVersion: 1 }),
    );
  });

  it("routes side-panel field edits through reshapeNode, preserving untouched metadata", () => {
    instance.currentNode = null;
    instance.findEle.mockReturnValue({
      nodeObj: {
        id: "root",
        topic: "Projekt",
        metadata: { status: "active", owner: "Bára", lockedBy: null, sourceMeetingIds: ["m1"] },
      },
    });
    const { ref } = renderCanvas();
    act(() => {
      ref.current!.patchNode("root", { status: "done" });
    });
    expect(instance.reshapeNode).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        metadata: { status: "done", owner: "Bára", lockedBy: null, sourceMeetingIds: ["m1"] },
      }),
    );
  });

  it("destroys the instance on unmount", () => {
    const { unmount } = renderCanvas();
    unmount();
    expect(instance.destroy).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapCanvas" --watchAll=false`

Expected: FAIL — `MindMapCanvas` still exports the React Flow component and has no ref handle.

- [ ] **Step 3: Write the implementation**

Replace `frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx` entirely:

```tsx
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useRef } from "react";
import MindElixir from "mind-elixir";
import type { MindElixirInstance } from "mind-elixir";
import "mind-elixir/style";
import { useTheme } from "../../../../contexts/ThemeContext";
import { MindMapDocument, MindMapNode } from "./mindMapDocument";
import {
  DEFAULT_METADATA,
  displayFieldsFor,
  fromMindElixir,
  MindMapNodeMetadata,
  MindMapNodeObj,
  toMindElixir,
} from "./mindElixirMapping";
import { themeFor } from "./mindElixirTheme";

export type MindMapNodePatch = Partial<Pick<MindMapNode, "title" | "notes" | "owner" | "status">>;

export interface MindMapCanvasHandle {
  getDocument: () => MindMapDocument | null;
  expandAll: () => void;
  collapseAll: () => void;
  fit: () => void;
  addChild: () => void;
  addSibling: () => void;
  undo: () => void;
  patchNode: (nodeId: string, patch: MindMapNodePatch) => void;
  exportPng: () => Promise<Blob | null>;
  exportSvg: () => Blob | null;
}

export interface MindMapCanvasProps {
  /** Used once, on mount. Later server documents arrive via `documentRevision`. */
  initialDocument: MindMapDocument;
  /**
   * Opaque token identifying the server document currently loaded. Changing it
   * reloads the map; keeping it stable leaves the user's in-progress edits alone.
   * The page passes the raw `documentJson` string.
   */
  documentRevision: string;
  isReadOnly: boolean;
  /** Any edit the user made — the page turns this into `isDirty`. */
  onChange: () => void;
  onSelectNode: (nodeId: string | null) => void;
}

const MindMapCanvas = forwardRef<MindMapCanvasHandle, MindMapCanvasProps>(function MindMapCanvas(
  { initialDocument, documentRevision, isReadOnly, onChange, onSelectNode },
  ref,
) {
  const { theme } = useTheme();
  const containerRef = useRef<HTMLDivElement>(null);
  const instanceRef = useRef<MindElixirInstance | null>(null);
  const loadedRevisionRef = useRef<string>(documentRevision);
  // The document the editor was loaded with — supplies schemaVersion and the
  // tombstone list, neither of which mind-elixir knows anything about.
  const baseDocumentRef = useRef<MindMapDocument>(initialDocument);

  // Latest callbacks, so the mount effect can stay dependency-free and the
  // instance is never torn down just because the page re-rendered.
  const onChangeRef = useRef(onChange);
  const onSelectNodeRef = useRef(onSelectNode);
  useEffect(() => {
    onChangeRef.current = onChange;
    onSelectNodeRef.current = onSelectNode;
  }, [onChange, onSelectNode]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return undefined;

    const instance = new MindElixir({
      el: container,
      direction: MindElixir.SIDE,
      allowUndo: true,
      // Off for two independent reasons, both load-bearing:
      //  1. Its menu creates arrows ("link") and summaries. MindMapDocument stores
      //     neither, and fromMindElixir reads only `nodeData` — anything a user
      //     created that way would be silently discarded on the next save. The
      //     ContextMenuOption type can disable `link` but NOT `summary`, so there
      //     is no partial setting that closes the hole.
      //  2. mind-elixir ships no Czech language pack (cn/en/ru/ja/pt/it/es/fr/ko/
      //     ro/da/fi/de/nl only), and this UI is Czech throughout.
      // Our toolbar covers add-sibling/add-child/undo, ⌫ deletes, and ⌘↑/⌘↓ reorder.
      contextMenu: false,
      toolBar: false, // we render our own Czech toolbar
      keypress: true,
      theme: themeFor(theme === "dark" ? "dark" : "light"),
    });
    instance.init(toMindElixir(baseDocumentRef.current));
    instanceRef.current = instance;

    const handleEdit = () => onChangeRef.current();
    const handleSelect = (node: { id: string }) => onSelectNodeRef.current(node.id);
    const handleUnselect = () => onSelectNodeRef.current(null);

    instance.bus.addListener("operation", handleEdit);
    // Collapsing a branch is a persisted change (`collapsed`), but it is NOT an
    // `operation` — it has its own event.
    instance.bus.addListener("expandNode", handleEdit);
    instance.bus.addListener("selectNewNode", handleSelect);
    instance.bus.addListener("unselectNodes", handleUnselect);

    return () => {
      instance.bus.removeListener("operation", handleEdit);
      instance.bus.removeListener("expandNode", handleEdit);
      instance.bus.removeListener("selectNewNode", handleSelect);
      instance.bus.removeListener("unselectNodes", handleUnselect);
      instance.destroy();
      instanceRef.current = null;
    };
    // Mount once. Data, theme and read-only state are pushed by the effects below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Adopt a new server document. The page only bumps the revision when it is safe
  // to do so (no unsaved edits), so this never clobbers work in progress.
  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance || documentRevision === loadedRevisionRef.current) return;
    loadedRevisionRef.current = documentRevision;
    baseDocumentRef.current = initialDocument;
    instance.refresh(toMindElixir(initialDocument));
    instance.clearHistory?.();
  }, [documentRevision, initialDocument]);

  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance) return;
    if (isReadOnly) instance.disableEdit();
    else instance.enableEdit();
  }, [isReadOnly]);

  useEffect(() => {
    const instance = instanceRef.current;
    if (!instance) return;
    instance.changeTheme(themeFor(theme === "dark" ? "dark" : "light"), true);
  }, [theme]);

  const currentTopic = useCallback(() => {
    const instance = instanceRef.current;
    return instance?.currentNode ?? null;
  }, []);

  useImperativeHandle(
    ref,
    (): MindMapCanvasHandle => ({
      getDocument: () => {
        const instance = instanceRef.current;
        if (!instance) return null;
        return fromMindElixir(instance.getData(), baseDocumentRef.current);
      },
      expandAll: () => {
        const instance = instanceRef.current;
        if (instance) instance.expandNodeAll(instance.findEle(instance.nodeData.id), true);
      },
      collapseAll: () => {
        const instance = instanceRef.current;
        if (!instance) return;
        // Collapse everything, then re-open the root so the map never disappears.
        instance.expandNodeAll(instance.findEle(instance.nodeData.id), false);
        instance.expandNode(instance.findEle(instance.nodeData.id), true);
      },
      fit: () => {
        instanceRef.current?.toCenter();
        instanceRef.current?.scaleFit();
      },
      addChild: () => {
        const topic = currentTopic();
        if (topic) void instanceRef.current?.addChild(topic);
      },
      addSibling: () => {
        const topic = currentTopic();
        if (topic) void instanceRef.current?.insertSibling("after", topic);
      },
      undo: () => instanceRef.current?.undo(),
      patchNode: (nodeId, patch) => {
        const instance = instanceRef.current;
        if (!instance) return;
        const topic = instance.findEle(nodeId);
        if (!topic) return;
        const nodeObj = topic.nodeObj as MindMapNodeObj;
        const previous: MindMapNodeMetadata = nodeObj.metadata ?? DEFAULT_METADATA;
        // reshapeNode replaces `metadata` wholesale, so merge before writing —
        // otherwise editing the owner would silently drop lockedBy and provenance.
        const metadata: MindMapNodeMetadata = {
          status: patch.status ?? previous.status,
          owner: patch.owner !== undefined ? patch.owner : previous.owner,
          lockedBy: previous.lockedBy,
          sourceMeetingIds: previous.sourceMeetingIds,
        };
        const notes = patch.notes !== undefined ? patch.notes : nodeObj.note ?? null;
        void instance.reshapeNode(topic, {
          ...(patch.title !== undefined ? { topic: patch.title } : {}),
          note: notes ?? undefined,
          metadata,
          ...displayFieldsFor(metadata, notes),
        });
      },
      exportPng: async () => (await instanceRef.current?.exportPng()) ?? null,
      exportSvg: () => instanceRef.current?.exportSvg() ?? null,
    }),
    [currentTopic],
  );

  return (
    <div
      data-testid="mindmap-canvas"
      ref={containerRef}
      className="h-full w-full"
    />
  );
});

export default MindMapCanvas;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapCanvas" --watchAll=false`

Expected: PASS, 11 tests.

- [ ] **Step 5: Verify the event names against the real library**

The stubbed bus in the test proves our wiring, not mind-elixir's event names. Those came from the published `EventMap` type, but which event fires on a plain click (`selectNewNode` vs `selectNodes`) is not something the type tells us.

Temporarily add to the mount effect, run the app (`npm start`), click and edit nodes, and read the console:

```ts
(["operation", "selectNewNode", "selectNodes", "unselectNodes", "expandNode"] as const).forEach((type) =>
  instance.bus.addListener(type, (...args: unknown[]) => console.log("[me]", type, args)),
);
```

Confirm that clicking a node logs `selectNewNode`, clicking the background logs `unselectNodes`, collapsing logs `expandNode`, and editing logs `operation`. If a plain click fires `selectNodes` instead, add a `selectNodes` listener that maps a single-element array to that node's id and leaves multi-select alone. Remove the logging before committing.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapCanvas.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapCanvas.test.tsx
git commit -m "feat: rebuild MindMapCanvas on mind-elixir"
```

---

### Task 5: Detail page — flip state ownership to the instance

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx:17-28` (canvas mock) plus new cases

**Interfaces:**
- Consumes: `MindMapCanvasHandle`, `MindMapCanvasProps` (Task 4)
- Produces: no new exports; `MindMapSidePanel` now receives `onUpdateNode` backed by `canvasRef.current.patchNode`.

The invariants that must survive this rewrite — each currently carries an explanatory comment in the file, and each has a test below:

1. A background poll must never discard unsaved edits (`isDirty` gates adoption).
2. After a successful save, the canonical server response is written into the React Query cache *and* becomes the loaded revision, so the adoption effect does not revert the save.
3. Regenerating or attaching a meeting while dirty is refused with a toast.
4. A malformed `documentJson` shows an error state instead of crashing the ErrorBoundary.

- [ ] **Step 1: Update the canvas mock in the page test**

Replace the `jest.mock("../MindMapCanvas", ...)` block at `__tests__/MindMapDetailPage.test.tsx:17-28` with one that mimics the ref handle:

```tsx
const canvasHandle = {
  getDocument: jest.fn(),
  expandAll: jest.fn(),
  collapseAll: jest.fn(),
  fit: jest.fn(),
  addChild: jest.fn(),
  addSibling: jest.fn(),
  remove: jest.fn(),
  undo: jest.fn(),
  patchNode: jest.fn(),
  exportPng: jest.fn(),
  exportSvg: jest.fn(),
};

jest.mock("../MindMapCanvas", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: React.forwardRef(
      (
        props: {
          initialDocument: { nodes: { id: string; title: string }[] };
          documentRevision: string;
          onSelectNode: (id: string) => void;
          onChange: () => void;
        },
        ref: React.Ref<unknown>,
      ) => {
        React.useImperativeHandle(ref, () => canvasHandle);
        return (
          <div data-testid="mindmap-canvas-stub" data-revision={props.documentRevision}>
            {props.initialDocument.nodes.map((n) => (
              <button key={n.id} type="button" onClick={() => props.onSelectNode(n.id)}>
                {n.title}
              </button>
            ))}
            <button type="button" data-testid="stub-edit" onClick={() => props.onChange()}>
              edit
            </button>
          </div>
        );
      },
    ),
  };
});
```

- [ ] **Step 2: Replace the adoption-guard test, which this change would otherwise make vacuous**

The existing test at `__tests__/MindMapDetailPage.test.tsx:102-137` ("does not let a fresher server document clobber an unsaved local edit") types into `mindmap-panel-title-input` and asserts the input still holds the typed text after a newer document lands in the cache.

After Task 6 that input holds **local draft state**, so it would keep the typed text whether or not adoption was correctly suppressed — the test would pass even with the guard removed. Replace its body (keep the `it(...)` title and the surrounding mock setup) so it asserts the thing that now actually proves the guard: that the canvas is never handed a new revision.

Delete lines 113-136 of that test (from `await selectRootNode();` to the final `expect`) and put in their place:

```tsx
    const stub = await screen.findByTestId("mindmap-canvas-stub");
    const revisionBeforeEdit = stub.getAttribute("data-revision");

    // The canvas reports an edit — the page is now dirty.
    fireEvent.click(screen.getByTestId("stub-edit"));

    // Simulate a fresher document landing in the cache (e.g. a 3s "Updating" poll,
    // or a just-finished Claude rewrite) while the user has unsaved work. React
    // Query notifies observers a macrotask after setQueryData, not synchronously
    // within `act`'s callback — flush it before asserting, otherwise the check
    // below would run against a render that hasn't happened yet and pass
    // vacuously either way.
    await act(async () => {
      queryClient.setQueryData(MIND_MAPS_KEYS.detail(MAP_ID), {
        ...initialDetail,
        documentJson: JSON.stringify(buildDoc({ nodes: [{ ...buildDoc().nodes[0], title: "Ze serveru" }] })),
      });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    // Reloading the canvas would throw away everything the user has done since.
    expect(screen.getByTestId("mindmap-canvas-stub").getAttribute("data-revision")).toBe(
      revisionBeforeEdit,
    );
    expect(screen.getByText(/novější verze mapy/i)).toBeInTheDocument();
```

- [ ] **Step 3: Fix the existing save test for commit-on-blur**

The test at `__tests__/MindMapDetailPage.test.tsx:139-186` does `fireEvent.change(titleInput, ...)` and then clicks Save. In a real browser, mousedown on the button blurs the input before the click handler runs, so the edit commits. `fireEvent.click` in jsdom does **not** blur, so the edit would never reach the canvas.

Add an explicit blur between the change and the click:

```tsx
    fireEvent.change(titleInput, { target: { value: "Upraveno" } });
    // Commit-on-blur (see MindMapSidePanel): a real browser blurs on the Save
    // button's mousedown, jsdom does not.
    fireEvent.blur(titleInput);
```

and make the canvas stub return the edited document for this test, since the page now saves what the canvas reports rather than its own copy:

```tsx
    canvasHandle.getDocument.mockReturnValue(canonicalDoc);
```

- [ ] **Step 4: Write the new failing tests**

Append to `__tests__/MindMapDetailPage.test.tsx`. These use the file's real helpers — `createMockApiClient(BASE_URL)` returning `{ mockClient, mockFetch }`, `newQueryClient()`, and `renderPage(queryClient)` which takes the client as an argument:

```tsx
  it("marks the map dirty when the canvas reports an edit", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(buildDetail());
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await screen.findByTestId("mindmap-canvas-stub");

    expect(screen.getByTestId("mindmap-save-button")).toBeDisabled();
    fireEvent.click(screen.getByTestId("stub-edit"));
    expect(screen.getByTestId("mindmap-save-button")).toBeEnabled();
  });

  it("saves the document read back out of the canvas, not a stale React copy", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const edited = buildDoc({
      nodes: [
        buildDoc().nodes[0],
        {
          id: "tmp-1", parentId: "root", title: "Nový", notes: null, status: "active",
          owner: null, lockedBy: null, sourceMeetingIds: [], position: null, collapsed: false,
        },
      ],
    });
    canvasHandle.getDocument.mockReturnValue(edited);

    let savedJson: string | null = null;
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? "GET").toUpperCase();
      if (method === "GET" && url === DETAIL_URL) return jsonResponse(buildDetail());
      if (method === "PUT" && url === SAVE_URL) {
        savedJson = JSON.parse(init!.body as string).documentJson;
        return jsonResponse({ documentJson: savedJson });
      }
      throw new Error(`Unexpected fetch: ${method} ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await screen.findByTestId("mindmap-canvas-stub");

    fireEvent.click(screen.getByTestId("stub-edit"));
    fireEvent.click(screen.getByTestId("mindmap-save-button"));

    await waitFor(() => expect(savedJson).not.toBeNull());
    // The second node exists only inside the canvas — a page that still saved its
    // own React copy would send one node here.
    expect(JSON.parse(savedJson!).nodes).toHaveLength(2);
  });

  it("hands the canvas the new revision once the edits are saved", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const canonicalDoc = buildDoc({
      nodes: [{ ...buildDoc().nodes[0], title: "Upraveno", lockedBy: "ondra@anela.cz" }],
    });
    canvasHandle.getDocument.mockReturnValue(canonicalDoc);
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? "GET").toUpperCase();
      if (method === "GET" && url === DETAIL_URL) return jsonResponse(buildDetail());
      if (method === "PUT" && url === SAVE_URL) {
        return jsonResponse({ documentJson: JSON.stringify(canonicalDoc) });
      }
      throw new Error(`Unexpected fetch: ${method} ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    const stub = await screen.findByTestId("mindmap-canvas-stub");
    const revisionBeforeSave = stub.getAttribute("data-revision");

    fireEvent.click(screen.getByTestId("stub-edit"));
    fireEvent.click(screen.getByTestId("mindmap-save-button"));

    await waitFor(() =>
      expect(screen.getByTestId("mindmap-canvas-stub").getAttribute("data-revision")).not.toBe(
        revisionBeforeSave,
      ),
    );
  });
```

Add `waitFor` to the `@testing-library/react` import at the top of the file if it is not already there.

- [ ] **Step 5: Run to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapDetailPage" --watchAll=false`

Expected: FAIL — the page still owns `localDoc` and passes the old canvas props.

- [ ] **Step 6: Rewrite the page's state layer**

In `MindMapDetailPage.tsx`, delete the `useMindMapUndo`/`useMindMapKeyboard` imports and their usages, delete `applyEdit`/`commitEdit`/`mutate`, delete every `addChildNode`/`addSiblingNode`/`indentNode`/`outdentNode`/`moveNode`/`toggleCollapsed`/`setAllCollapsed`/`renameNode`/`deleteNode`/`updateNodeFields` import, and delete the `editingNodeId` state and the `⌘S` effect's dependency on `localDoc`.

Replace the state block and handlers with:

```tsx
  const canvasRef = useRef<MindMapCanvasHandle>(null);
  const [loadedJson, setLoadedJson] = useState<string | null>(null);
  const [loadedDoc, setLoadedDoc] = useState<MindMapDocument | null>(null);
  const [panelDoc, setPanelDoc] = useState<MindMapDocument | null>(null);
  const [isDirty, setIsDirty] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [isHelpOpen, setIsHelpOpen] = useState(false);
  const [hasDocumentParseError, setHasDocumentParseError] = useState(false);

  const isReadOnly = detail?.status === "Updating";

  // Adopt a server document only when there is nothing unsaved to lose. `loadedJson`
  // doubles as the canvas's revision token: bumping it is what reloads the map.
  useEffect(() => {
    if (!detail) return;
    if (isDirty || detail.documentJson === loadedJson) return;
    try {
      const parsed = parseDocument(detail.documentJson);
      setLoadedDoc(parsed);
      setPanelDoc(parsed);
      setLoadedJson(detail.documentJson);
      setHasDocumentParseError(false);
    } catch {
      setHasDocumentParseError(true);
    }
  }, [detail, isDirty, loadedJson]);

  // Any edit inside the canvas. Pulling a fresh snapshot here is what keeps the
  // side panel showing the node's real current values.
  const handleCanvasChange = useCallback(() => {
    setIsDirty(true);
    const snapshot = canvasRef.current?.getDocument();
    if (snapshot) setPanelDoc(snapshot);
  }, []);

  const handleSelectNode = useCallback((nodeId: string | null) => {
    setSelectedNodeId(nodeId);
    const snapshot = canvasRef.current?.getDocument();
    if (snapshot) setPanelDoc(snapshot);
  }, []);

  const handleUpdateNode = useCallback(
    (nodeId: string, patch: MindMapNodePatch) => {
      if (isReadOnly) return;
      canvasRef.current?.patchNode(nodeId, patch);
    },
    [isReadOnly],
  );
```

Rewrite `handleSave` so the document comes from the canvas:

```tsx
  const handleSave = useCallback(async (): Promise<boolean> => {
    const documentToSave = canvasRef.current?.getDocument();
    if (!id || !documentToSave) return false;

    let result: { documentJson: string };
    try {
      result = await saveDocument.mutateAsync({
        mindMapId: id,
        documentJson: JSON.stringify(documentToSave),
      });
    } catch {
      toast.error("Uložení mapy se nezdařilo");
      return false;
    }

    // The save succeeded server-side. Write the canonical result into the query
    // cache and adopt it as the loaded revision in the same pass: if `loadedJson`
    // stayed behind, the adoption effect would see a "newer" document the moment
    // isDirty flips false and reload the map out from under the user — visibly
    // undoing a save that actually worked.
    queryClient.setQueryData<MindMapDetail>(MIND_MAPS_KEYS.detail(id), (old) =>
      old ? { ...old, documentJson: result.documentJson } : old,
    );
    setIsDirty(false);

    try {
      const parsed = parseDocument(result.documentJson);
      setLoadedDoc(parsed);
      setPanelDoc(parsed);
      setLoadedJson(result.documentJson);
      toast.success("Mapa uložena");
    } catch {
      toast.error("Mapa byla uložena, ale odpověď serveru se nepodařilo zobrazit. Načtěte stránku znovu.");
    }
    return true;
  }, [id, saveDocument, queryClient]);
```

Update the two dirty-guards to read `isDirty` unchanged (`handleRegenerate` and the meetings tab already do), and change the "newer server version" banner condition to:

```tsx
  const hasNewerServerVersion = isDirty && detail.documentJson !== loadedJson;
```

Finally, render the canvas with the new contract:

```tsx
  {loadedDoc && (
    <MindMapCanvas
      ref={canvasRef}
      initialDocument={loadedDoc}
      documentRevision={loadedJson ?? ""}
      isReadOnly={isReadOnly}
      onChange={handleCanvasChange}
      onSelectNode={handleSelectNode}
    />
  )}
```

and pass `panelDoc` (not `localDoc`) to `MindMapSidePanel`.

Import `MindMapCanvas, { MindMapCanvasHandle, MindMapNodePatch }` from `./MindMapCanvas`, and type `handleUpdateNode`'s `patch` parameter as `MindMapNodePatch`.

Leave `onAddChild`, `onDeleteNode` and `onToggleCollapsed` on the `MindMapSidePanel` element for now — Task 6 removes those props from the panel and from this call site together. Point them at no-ops here so the page compiles in the meantime:

```tsx
            onAddChild={() => {}}
            onDeleteNode={() => {}}
            onToggleCollapsed={() => {}}
```

- [ ] **Step 7: Run the page tests**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapDetailPage" --watchAll=false`

Expected: PASS, including the pre-existing invariant tests about poll adoption, save-revert, read-only and parse errors.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapDetailPage.test.tsx
git commit -m "refactor: let the mind-elixir instance own live mind map editing state"
```

---

### Task 6: Side panel edits without a re-layout per keystroke

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapSidePanel.tsx`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx`

**Interfaces:**
- Consumes: `onUpdateNode(nodeId, patch)` — now backed by `reshapeNode`
- Produces: no new exports

Under React Flow, every keystroke patched a plain object. Under mind-elixir, every keystroke would call `reshapeNode`, which re-renders and re-lays-out the map. The title and notes fields therefore become locally controlled and commit on blur.

- [ ] **Step 1: Write the failing test**

Append to `__tests__/MindMapSidePanel.test.tsx`:

```tsx
it("does not push a document change on every keystroke in the title field", () => {
  const onUpdateNode = jest.fn();
  renderPanel({ onUpdateNode, selectedNodeId: "a" });

  const input = screen.getByTestId("mindmap-panel-title-input");
  fireEvent.change(input, { target: { value: "Nov" } });
  fireEvent.change(input, { target: { value: "Nový" } });

  // Each keystroke would otherwise trigger a full mind-elixir re-layout.
  expect(onUpdateNode).not.toHaveBeenCalled();
  expect(input).toHaveValue("Nový");
});

it("commits the title when the field loses focus", () => {
  const onUpdateNode = jest.fn();
  renderPanel({ onUpdateNode, selectedNodeId: "a" });

  const input = screen.getByTestId("mindmap-panel-title-input");
  fireEvent.change(input, { target: { value: "Nový název" } });
  fireEvent.blur(input);

  expect(onUpdateNode).toHaveBeenCalledWith("a", { title: "Nový název" });
});

it("does not commit when the text is unchanged", () => {
  const onUpdateNode = jest.fn();
  renderPanel({ onUpdateNode, selectedNodeId: "a" });
  fireEvent.blur(screen.getByTestId("mindmap-panel-title-input"));
  expect(onUpdateNode).not.toHaveBeenCalled();
});

it("shows each node's own values, and an abandoned draft does not leak across selections", () => {
  // The draft is reset by keying the field on the node id; without that key, typing
  // into one node and clicking another would show the first node's text.
  const { unmount } = renderPanel({ selectedNodeId: "a" });
  fireEvent.change(screen.getByTestId("mindmap-panel-title-input"), { target: { value: "rozepsáno" } });
  unmount();

  renderPanel({ selectedNodeId: "b" });
  expect(screen.getByTestId("mindmap-panel-title-input")).toHaveValue("List B");
});

it("still commits status immediately — a select has no intermediate states", () => {
  const onUpdateNode = jest.fn();
  renderPanel({ onUpdateNode, selectedNodeId: "a" });
  fireEvent.change(screen.getByLabelText("Stav"), { target: { value: "done" } });
  expect(onUpdateNode).toHaveBeenCalledWith("a", { status: "done" });
});
```

`renderPanel(overrides: Partial<MindMapSidePanelProps>)` already exists at `__tests__/MindMapSidePanel.test.tsx:70` — use it as-is. The node ids `"a"` (title `"Větev A"`) and `"b"` (title `"List B"`) come from that file's existing `buildDoc()`; check them before writing the assertions and use whatever it actually defines.

- [ ] **Step 2: Run to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapSidePanel" --watchAll=false`

Expected: FAIL — the title field currently calls `onUpdateNode` on every change.

- [ ] **Step 3: Add a commit-on-blur text field to the panel**

In `MindMapSidePanel.tsx`, add above `NodeTab`:

```tsx
interface CommitOnBlurFieldProps {
  id: string;
  label: string;
  value: string;
  disabled: boolean;
  rows?: number;
  testId?: string;
  onCommit: (value: string) => void;
}

/**
 * Text field that keeps its own draft and only reports on blur. Each commit reaches
 * mind-elixir's reshapeNode, which re-renders and re-lays-out the whole map — doing
 * that per keystroke makes typing visibly stutter.
 * `key`ing this component by node id (see NodeTab) is what resets the draft when
 * the user selects a different node.
 */
const CommitOnBlurField: React.FC<CommitOnBlurFieldProps> = ({
  id,
  label,
  value,
  disabled,
  rows,
  testId,
  onCommit,
}) => {
  const [draft, setDraft] = useState(value);
  const commit = () => {
    if (draft !== value) onCommit(draft);
  };
  const props = {
    id,
    value: draft,
    disabled,
    "data-testid": testId,
    className: INPUT_CLASS,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setDraft(e.target.value),
    onBlur: commit,
  };
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1">
        {label}
      </label>
      {rows ? <textarea {...props} rows={rows} /> : <input {...props} type="text" />}
    </div>
  );
};
```

Replace the title, notes and owner blocks inside `NodeTab` with:

```tsx
      <CommitOnBlurField
        key={`${node.id}-title`}
        id="mindmap-node-title"
        label="Název"
        testId="mindmap-panel-title-input"
        value={node.title}
        disabled={isReadOnly}
        onCommit={(title) => onUpdateNode(node.id, { title })}
      />

      <CommitOnBlurField
        key={`${node.id}-notes`}
        id="mindmap-node-notes"
        label="Poznámky"
        rows={4}
        value={node.notes ?? ""}
        disabled={isReadOnly}
        onCommit={(notes) => onUpdateNode(node.id, { notes: notes || null })}
      />

      <CommitOnBlurField
        key={`${node.id}-owner`}
        id="mindmap-node-owner"
        label="Vlastník"
        value={node.owner ?? ""}
        disabled={isReadOnly}
        onCommit={(owner) => onUpdateNode(node.id, { owner: owner || null })}
      />
```

Leave the status `<select>` calling `onUpdateNode` directly — a select commits atomically.

Remove the "Přidat poduzel" / "Sbalit" / "Smazat uzel" buttons from `NodeTab`, the `onAddChild`, `onDeleteNode`, `onToggleCollapsed` props from `MindMapSidePanelProps` and `NodeTabProps`, and the matching no-op props from the `<MindMapSidePanel>` element in `MindMapDetailPage.tsx` (added in Task 5, Step 6). The toolbar owns adding, the node's own expander owns collapsing and ⌫ owns deleting; a second path that bypasses the instance would desync it.

- [ ] **Step 4: Run the panel tests**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapSidePanel" --watchAll=false`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapSidePanel.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapSidePanel.test.tsx
git commit -m "refactor: commit side panel text edits on blur instead of per keystroke"
```

---

### Task 7: Toolbar, help sheet and export

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapToolbar.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapHelpSheet.tsx`
- Modify: `frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx` (wire the handlers)
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapToolbar.test.tsx` (create)

**Interfaces:**
- Consumes: `MindMapCanvasHandle` (Task 4)
- Produces: `MindMapToolbarProps` gains `onExportPng`, `onExportSvg`; loses `canUndo`

- [ ] **Step 1: Write the failing test**

Create `frontend/src/components/pages/automation/mindmaps/__tests__/MindMapToolbar.test.tsx`:

```tsx
import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import MindMapToolbar from "../MindMapToolbar";

const handlers = () => ({
  onExpandAll: jest.fn(),
  onCollapseAll: jest.fn(),
  onFit: jest.fn(),
  onAddSibling: jest.fn(),
  onAddChild: jest.fn(),
  onUndo: jest.fn(),
  onOpenHelp: jest.fn(),
  onExportPng: jest.fn(),
  onExportSvg: jest.fn(),
});

function renderToolbar(overrides: Partial<React.ComponentProps<typeof MindMapToolbar>> = {}) {
  const props = { isReadOnly: false, hasSelection: true, ...handlers(), ...overrides };
  render(<MindMapToolbar {...props} />);
  return props;
}

it("wires each toolbar action to its handler", () => {
  const props = renderToolbar();
  fireEvent.click(screen.getByText("Rozbalit"));
  fireEvent.click(screen.getByText("Sbalit"));
  fireEvent.click(screen.getByTestId("mindmap-fit-button"));
  fireEvent.click(screen.getByTestId("mindmap-undo"));
  expect(props.onExpandAll).toHaveBeenCalled();
  expect(props.onCollapseAll).toHaveBeenCalled();
  expect(props.onFit).toHaveBeenCalled();
  expect(props.onUndo).toHaveBeenCalled();
});

it("offers PNG and SVG export", () => {
  const props = renderToolbar();
  fireEvent.click(screen.getByTestId("mindmap-export-png"));
  fireEvent.click(screen.getByTestId("mindmap-export-svg"));
  expect(props.onExportPng).toHaveBeenCalled();
  expect(props.onExportSvg).toHaveBeenCalled();
});

it("keeps export available on a read-only map but disables the editing actions", () => {
  renderToolbar({ isReadOnly: true });
  expect(screen.getByTestId("mindmap-export-png")).toBeEnabled();
  expect(screen.getByTestId("mindmap-add-child")).toBeDisabled();
  expect(screen.getByTestId("mindmap-undo")).toBeDisabled();
});

it("disables the add actions when nothing is selected", () => {
  renderToolbar({ hasSelection: false });
  expect(screen.getByTestId("mindmap-add-sibling")).toBeDisabled();
  expect(screen.getByTestId("mindmap-add-child")).toBeDisabled();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapToolbar" --watchAll=false`

Expected: FAIL — no export buttons, and `canUndo` is still a required prop.

- [ ] **Step 3: Update the toolbar**

In `MindMapToolbar.tsx`: drop `canUndo` from `MindMapToolbarProps` (mind-elixir owns the history and does not expose its depth — `undo()` is a no-op on an empty stack), add `onExportPng: () => void` and `onExportSvg: () => void`, change the undo button's `disabled` to just `isReadOnly`, and add before the help button:

```tsx
    <Separator />

    <button
      type="button"
      data-testid="mindmap-export-png"
      className={BUTTON_CLASS}
      onClick={onExportPng}
      title="Stáhnout mapu jako PNG"
    >
      PNG
    </button>
    <button
      type="button"
      data-testid="mindmap-export-svg"
      className={BUTTON_CLASS}
      onClick={onExportSvg}
      title="Stáhnout mapu jako SVG"
    >
      SVG
    </button>
```

- [ ] **Step 4: Wire the handlers in the page**

In `MindMapDetailPage.tsx`, add above the return:

```tsx
  const downloadBlob = (blob: Blob, extension: string) => {
    const url = URL.createObjectURL(blob);
    const link = window.document.createElement("a");
    link.href = url;
    link.download = `${detail?.name ?? "mapa"}.${extension}`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const handleExportPng = async () => {
    const blob = await canvasRef.current?.exportPng();
    if (!blob) {
      toast.error("Export mapy do PNG se nezdařil");
      return;
    }
    downloadBlob(blob, "png");
  };

  const handleExportSvg = () => {
    const blob = canvasRef.current?.exportSvg();
    if (!blob) {
      toast.error("Export mapy do SVG se nezdařil");
      return;
    }
    downloadBlob(blob, "svg");
  };
```

and replace the toolbar element with:

```tsx
              <MindMapToolbar
                isReadOnly={isReadOnly}
                hasSelection={selectedNodeId !== null}
                onExpandAll={() => canvasRef.current?.expandAll()}
                onCollapseAll={() => canvasRef.current?.collapseAll()}
                onFit={() => canvasRef.current?.fit()}
                onAddSibling={() => canvasRef.current?.addSibling()}
                onAddChild={() => canvasRef.current?.addChild()}
                onUndo={() => canvasRef.current?.undo()}
                onOpenHelp={() => setIsHelpOpen(true)}
                onExportPng={handleExportPng}
                onExportSvg={handleExportSvg}
              />
```

- [ ] **Step 5: Rewrite the help sheet's shortcut table**

Our `useMindMapKeyboard` is gone; the bindings are mind-elixir's. Replace the `SHORTCUTS` array in `MindMapHelpSheet.tsx` with:

```tsx
const SHORTCUTS: Array<[string, string]> = [
  ["klik", "vybrat uzel"],
  ["dvojklik", "začít psát do uzlu"],
  ["Enter", "nový uzel vedle vybraného"],
  ["Tab", "nový uzel pod vybraný"],
  ["⌫", "smazat vybraný uzel i s podřízenými"],
  ["mezerník", "sbalit / rozbalit větev"],
  ["↑ ↓ ← →", "chodit po mapě"],
  ["⌘Z / ⌘⇧Z", "zpět / znovu"],
  ["⌘S", "uložit mapu"],
  ["táhnutí uzlu", "přesunout pod jiný uzel"],
];
```

and replace the closing paragraph with:

```tsx
        <p className="mt-4 text-xs text-gray-500 dark:text-graphite-muted">
          Rozložení mapy se dopočítává automaticky — kořen je uprostřed a větve se střídavě rozrůstají doprava a
          doleva. Uzly lze přetahovat pod jiné uzly; jejich poloha se neukládá.
        </p>
```

- [ ] **Step 6: Verify the bindings you just documented are real**

Run `npm start`, open a map, and confirm each row of that table. mind-elixir's key handling is its own; anything that does not behave as written must be corrected in the table, not left to mislead. Note in particular whether ⌫ deletes without confirmation — if it does, that is a behaviour change from the old side-panel delete and belongs in the migration notes.

- [ ] **Step 7: Run the toolbar tests**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="MindMapToolbar" --watchAll=false`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/MindMapToolbar.tsx \
        frontend/src/components/pages/automation/mindmaps/MindMapHelpSheet.tsx \
        frontend/src/components/pages/automation/mindmaps/MindMapDetailPage.tsx \
        frontend/src/components/pages/automation/mindmaps/__tests__/MindMapToolbar.test.tsx
git commit -m "feat: add PNG/SVG export and rewire the mind map toolbar to mind-elixir"
```

---

### Task 8: Delete the React Flow implementation

**Files:**
- Delete: `mindMapLayout.ts`, `mindMapTextMeasure.ts`, `mindMapFlow.ts`, `mindMapInteraction.ts`, `MindMapCurvedEdge.tsx`, `MindMapFlowNode.tsx`, `useMindMapKeyboard.ts`, `useMindMapUndo.ts`, `__tests__/mindMapLayout.test.ts`, `__tests__/mindMapFlow.test.ts`, `__tests__/useMindMapKeyboard.test.tsx`
- Modify: `frontend/package.json`, `frontend/src/components/pages/automation/mindmaps/mindMapDocument.ts`, `__tests__/mindMapDocument.test.ts`

**Interfaces:**
- Consumes: everything from Tasks 4–7 is in place
- Produces: a green build with no React Flow

- [ ] **Step 1: Delete the dead modules and their tests**

```bash
cd frontend/src/components/pages/automation/mindmaps
rm mindMapLayout.ts mindMapTextMeasure.ts mindMapFlow.ts mindMapInteraction.ts \
   MindMapCurvedEdge.tsx MindMapFlowNode.tsx useMindMapKeyboard.ts useMindMapUndo.ts \
   __tests__/mindMapLayout.test.ts __tests__/mindMapFlow.test.ts __tests__/useMindMapKeyboard.test.tsx
```

- [ ] **Step 2: Remove React Flow**

```bash
cd frontend
npm uninstall @xyflow/react --legacy-peer-deps
```

Verify nothing still imports it:

```bash
grep -rn "@xyflow/react" src/ && echo "STILL REFERENCED — fix before continuing" || echo "clean"
```

- [ ] **Step 3: Prune the now-unused document helpers**

The canvas owns structural editing now, so `mindMapDocument.ts` collapses to the type definitions plus `parseDocument`. Everything else lost its last caller when the React Flow modules were deleted: `visibleNodeIds` and `childrenOf` were only used by `mindMapLayout.ts` and `useMindMapKeyboard.ts`; `renameNode`, `updateNodeFields`, `toggleCollapsed`, `setAllCollapsed`, `addChildNode`, `addSiblingNode`, `indentNode`, `outdentNode`, `moveNode` and `deleteNode` were only used by `MindMapDetailPage.tsx`.

Confirm that before deleting — the list is only correct if Tasks 4–7 landed as written:

```bash
cd frontend
for fn in addChildNode addSiblingNode indentNode outdentNode moveNode setAllCollapsed \
          toggleCollapsed deleteNode renameNode updateNodeFields childrenOf visibleNodeIds; do
  count=$(grep -rn "\b$fn\b" src/ --include=*.ts --include=*.tsx \
          | grep -v "mindmaps/mindMapDocument.ts" \
          | grep -v "__tests__/mindMapDocument.test.ts" | wc -l | tr -d ' ')
  echo "$fn: $count"
done
```

Every line should print `0`. Delete each of those functions from `mindMapDocument.ts`, plus the private helpers that become orphaned with them (`withNodes`, `patchNode`, `newNode`, `reinsertAfter`, `descendantIds`), and delete their tests from `__tests__/mindMapDocument.test.ts`.

What must remain in `mindMapDocument.ts`: `MindMapNodeStatus`, `MindMapNodePosition`, `MindMapNode`, `SuppressedNode`, `MindMapDocument`, and `parseDocument`. What must remain in its test file: the `parseDocument` cases.

If any line prints non-zero, do not delete that function — investigate the caller instead. A non-zero count means an earlier task did not fully migrate away from it.

- [ ] **Step 4: Run the full suite**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false`

Expected: all suites pass. Any failure here is a real dangling reference — fix it rather than deleting the test.

- [ ] **Step 5: Run the build and lint gates**

Run:

```bash
cd frontend
CI=false npm run build
npm run lint 2>&1 | grep -c error
```

Expected: `Compiled successfully.` and an error count no higher than the 177 baseline.

- [ ] **Step 6: Commit**

```bash
git add -A frontend/
git commit -m "chore: remove the React Flow mind map implementation"
```

---

### Task 9: Update the e2e scenario

**Files:**
- Modify: `frontend/test/e2e/mindmaps/mindmap.spec.ts`

**Interfaces:**
- Consumes: the rendered DOM from Task 4 (`me-tpc` topics carrying `data-nodeid`)
- Produces: a scenario that still covers create → attach → stub generates → rename → lock → delete

The spec currently uses `getByTestId('mindmap-node')` (3 places), `mindmap-node-lock`, and a `dblclick` that opened the side panel. mind-elixir renders its own DOM, so all of those change. The lock is now the `🔒` glyph rendered from `icons` by `displayFieldsFor`.

- [ ] **Step 1: Replace the node selectors**

At the top of the `test.describe` block, add:

```ts
  // mind-elixir renders each topic as a <me-tpc> element carrying data-nodeid.
  const nodes = (page: import('@playwright/test').Page) => page.locator('me-tpc');
```

Then:

- Line 23 → `await expect(nodes(page)).toHaveCount(1);`
- Line 69 → `await expect(nodes(page)).toHaveCount(2, { timeout: 60000 });`
- Line 77 → `const generatedNode = nodes(page).filter({ hasText: 'Porada:' });`

- [ ] **Step 2: Replace the rename interaction**

The rename must go through the side panel (that path is what exercises the save → auto-lock behaviour). Selecting the node is now a single click, and the title field commits on blur (Task 6), so an explicit blur is required before saving:

```ts
      // Rename the generated node → auto-lock on save
      const generatedNode = nodes(page).filter({ hasText: 'Porada:' });
      await generatedNode.click();
      const titleInput = page.getByTestId('mindmap-panel-title-input');
      await titleInput.fill('Ručně upravený uzel');
      // The field only reports its value on blur — clicking straight to Save would
      // blur it too, but doing it explicitly keeps the failure mode obvious.
      await titleInput.blur();
      await page.getByTestId('mindmap-save-button').click();
      await expect(nodes(page).filter({ hasText: 'Ručně upravený uzel' })).toContainText('🔒', {
        timeout: 15000,
      });
```

- [ ] **Step 3: Leave the canvas visibility check alone**

`page.getByTestId('mindmap-canvas')` at line 22 still works — Task 4 keeps that test id on the container element.

- [ ] **Step 4: Run the scenario against staging**

This suite runs against **deployed** staging, so it cannot validate uncommitted changes. Merge and deploy first, then:

Run: `./scripts/run-playwright-tests.sh --grep "Mind maps"`

Expected: the scenario passes. If the node count assertion fails at 1 instead of 2, check whether `me-tpc` also matches nodes inside mind-elixir's context menu or hidden template markup; if so, scope the locator to `.map-container me-tpc`.

- [ ] **Step 5: Commit**

```bash
git add frontend/test/e2e/mindmaps/mindmap.spec.ts
git commit -m "test: point the mind map e2e scenario at mind-elixir's DOM"
```

---

### Task 10 (optional): Exact template curve parity

mind-elixir's default branches are already curved, so this is cosmetic. Do it only if the default curve reads as noticeably different from the template.

**Files:**
- Modify: `frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts`
- Test: `frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts`

**Interfaces:**
- Consumes: `MainLineParams` / `SubLineParams` from `mind-elixir`
- Produces: `generateMainBranch` / `generateSubBranch` on both themes

- [ ] **Step 1: Write the failing test**

```ts
import { MIND_MAP_LIGHT_THEME } from "../mindElixirTheme";

test("branch generators emit a cubic Bézier with control points at the horizontal midpoint", () => {
  const path = MIND_MAP_LIGHT_THEME.generateMainBranch!.call(null as never, {
    pT: 100, pL: 0, pW: 100, pH: 40,
    cT: 200, cL: 160, cW: 80, cH: 30,
    direction: "rhs",
    containerHeight: 800,
  });
  // Parent right edge (100, 120) → child left edge (160, 215), midpoint x = 130.
  expect(path).toBe("M100,120 C130,120 130,215 160,215");
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirTheme" --watchAll=false`

Expected: FAIL — `generateMainBranch` is undefined.

- [ ] **Step 3: Implement the generators**

Add to `mindElixirTheme.ts`:

```ts
import type { MainLineParams, SubLineParams } from "mind-elixir";

// The template's link: a cubic Bézier whose two control points sit on the
// horizontal midpoint between parent and child, so the line leaves the parent
// horizontally and arrives at the child horizontally.
function bezier({
  pT, pL, pW, pH, cT, cL, cW, cH, direction,
}: MainLineParams | SubLineParams): string {
  const isRight = direction === "rhs";
  const x1 = isRight ? pL + pW : pL;
  const x2 = isRight ? cL : cL + cW;
  const y1 = pT + pH / 2;
  const y2 = cT + cH / 2;
  const midX = (x1 + x2) / 2;
  return `M${x1},${y1} C${midX},${y1} ${midX},${y2} ${x2},${y2}`;
}

export function generateMainBranch(params: MainLineParams): string {
  return bezier(params);
}

export function generateSubBranch(params: SubLineParams): string {
  return bezier(params);
}
```

and set `generateMainBranch` / `generateSubBranch` on both theme objects.

- [ ] **Step 4: Run the test**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern="mindElixirTheme" --watchAll=false`

Expected: PASS.

- [ ] **Step 5: Compare against the template in the browser**

Run `npm start`, open a map with at least three levels, and compare with `docs/` or the original template HTML. If the curves now look right, keep this; if mind-elixir's default read better, revert the task.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pages/automation/mindmaps/mindElixirTheme.ts \
        frontend/src/components/pages/automation/mindmaps/__tests__/mindElixirTheme.test.ts
git commit -m "style: match the template's Bezier branch curve exactly"
```

---

## Behaviour changes to expect

These are intentional consequences of the migration, worth knowing before review:

| Before | After |
|---|---|
| Delete only via the side panel, behind a `window.confirm` | ⌫ deletes the selected node immediately; ⌘Z undoes it |
| Undo capped at 60 steps, ours | mind-elixir's own history; `clearHistory()` on every document reload |
| No drag | Drag a node onto another to re-parent it |
| Nothing | PNG/SVG export, multi-select, copy/paste |
| Side panel typing patched the document per keystroke | Title/notes/owner commit on blur |
| Side panel had "Přidat poduzel" / "Sbalit" / "Smazat uzel" | Add lives on the toolbar; collapse is the node's own expander; delete is ⌫ |
| Branch colours derived by us from branch index | mind-elixir assigns from `theme.palette` by branch index — same behaviour, its implementation |

## Rollback

Every task is a single commit and the backend is untouched, so `git revert` of the Task 1–9 range restores the React Flow implementation exactly. The stored `documentJson` is unchanged by this migration in either direction, so a rollback needs no data repair.
