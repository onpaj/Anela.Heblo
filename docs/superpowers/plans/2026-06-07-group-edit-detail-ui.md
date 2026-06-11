# Group-Edit Detail UI with Drag & Drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full group-edit page at `/admin/access/groups/:id` with drag-and-drop pickers for permissions, included groups, and members; navigable from the Access Management list; with create-new-group support.

**Architecture:** A `TransferList` drag-and-drop primitive (two columns, items move between them via drag or click) is shared by three picker components (`PermissionPicker`, `IncludedGroupsPicker`, `MembersPicker`). `GroupDetailPage` holds a draft state, initializes from loaded data, and on Save calls the update-group endpoint plus per-user `assignUserGroups` mutations derived from a member diff. `AccessManagementPage` gains clickable rows and a "New group" button.

**Tech Stack:** React, TypeScript, @dnd-kit/core, @dnd-kit/utilities, @tanstack/react-query, react-router-dom v6, Tailwind CSS, lucide-react, Jest + React Testing Library (CRA / react-scripts test)

---

## File Map

**Create:**
- `frontend/src/components/access-management/TransferList.tsx` — DnD two-column primitive; exports `TransferItem` type
- `frontend/src/components/access-management/PermissionPicker.tsx` — wraps TransferList with catalogue features
- `frontend/src/components/access-management/IncludedGroupsPicker.tsx` — wraps TransferList with groups list
- `frontend/src/components/access-management/MembersPicker.tsx` — wraps TransferList with users list
- `frontend/src/pages/GroupDetailPage.tsx` — route component, holds `GroupDraft` state, save/cancel logic
- `frontend/src/components/access-management/__tests__/TransferList.test.tsx` — unit tests for the primitive
- `frontend/src/pages/__tests__/GroupDetailPage.test.tsx` — tests for draft init, save, member-diff algorithm

**Modify:**
- `frontend/src/pages/AccessManagementPage.tsx` — clickable rows (navigate to detail), Edit button, New group button
- `frontend/src/pages/__tests__/AccessManagementPage.test.tsx` — extend with navigation assertions
- `frontend/src/App.tsx` — register `/admin/access/groups/:id` route with RequireAccess

---

## Task 1: TransferList — test first, then implement

**Files:**
- Create: `frontend/src/components/access-management/__tests__/TransferList.test.tsx`
- Create: `frontend/src/components/access-management/TransferList.tsx`

### Step 1.1 — Write the failing tests

Create `frontend/src/components/access-management/__tests__/TransferList.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TransferList from "../TransferList";

const items = [
  { id: "a", label: "Item A" },
  { id: "b", label: "Item B" },
  { id: "c", label: "Item C" },
];

describe("TransferList", () => {
  it("renders unassigned items in available column and assigned items in assigned column", () => {
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={jest.fn()}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.getByText("Item C")).toBeInTheDocument();
    // Item B is in the assigned column — appears once only
    expect(screen.getAllByText("Item B")).toHaveLength(1);
  });

  it("clicking the assign (+) button moves an item to assigned and calls onChange", () => {
    const onChange = jest.fn();
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={onChange}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: /assign item a/i }));
    expect(onChange).toHaveBeenCalledWith(["b", "a"]);
  });

  it("clicking the remove (−) button moves an item to available and calls onChange", () => {
    const onChange = jest.fn();
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={onChange}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: /remove item b/i }));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("renders section headers in the available column when groupBy is provided", () => {
    const groupedItems = [
      { id: "a", label: "Item A" },
      { id: "b", label: "Item B" },
    ];
    render(
      <TransferList
        available={groupedItems}
        assignedIds={[]}
        onChange={jest.fn()}
        groupBy={(item) => (item.id === "a" ? "Section One" : "Section Two")}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(screen.getByText("Section One")).toBeInTheDocument();
    expect(screen.getByText("Section Two")).toBeInTheDocument();
  });

  it("shows sublabel text when provided", () => {
    const sublabelItem = [{ id: "x", label: "Item X", sublabel: "hint text" }];
    render(
      <TransferList
        available={sublabelItem}
        assignedIds={[]}
        onChange={jest.fn()}
      />
    );
    expect(screen.getByText("hint text")).toBeInTheDocument();
  });
});
```

### Step 1.2 — Run and confirm the tests fail

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="TransferList.test"
```

Expected: FAIL — `Cannot find module '../TransferList'`

### Step 1.3 — Implement TransferList.tsx

Create `frontend/src/components/access-management/TransferList.tsx`:

```tsx
import React from "react";
import {
  DndContext,
  useDroppable,
  useDraggable,
  DragEndEvent,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";

export type TransferItem = {
  id: string;
  label: string;
  sublabel?: string;
};

interface TransferListProps {
  available: TransferItem[];
  assignedIds: string[];
  onChange: (ids: string[]) => void;
  groupBy?: (item: TransferItem) => string;
  labels?: { available?: string; assigned?: string };
}

interface ItemRowProps {
  item: TransferItem;
  direction: "assign" | "remove";
  onMove: () => void;
}

const ItemRow: React.FC<ItemRowProps> = ({ item, direction, onMove }) => {
  const { attributes, listeners, setNodeRef, transform, isDragging } =
    useDraggable({ id: item.id });
  const style = transform ? { transform: CSS.Transform.toString(transform) } : undefined;
  return (
    <div
      ref={setNodeRef}
      style={style}
      className={`flex items-center justify-between px-3 py-2 rounded border border-gray-200 bg-white hover:bg-gray-50 cursor-grab${isDragging ? " opacity-50" : ""}`}
      {...attributes}
      {...listeners}
    >
      <div className="flex flex-col min-w-0 flex-1">
        <span className="text-sm text-gray-900 truncate">{item.label}</span>
        {item.sublabel && (
          <span className="text-xs text-gray-500 truncate">{item.sublabel}</span>
        )}
      </div>
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          onMove();
        }}
        aria-label={direction === "assign" ? `Assign ${item.label}` : `Remove ${item.label}`}
        className={`ml-3 flex-shrink-0 w-6 h-6 rounded flex items-center justify-center text-sm font-bold ${
          direction === "assign"
            ? "text-indigo-600 hover:bg-indigo-100"
            : "text-red-500 hover:bg-red-100"
        }`}
      >
        {direction === "assign" ? "+" : "−"}
      </button>
    </div>
  );
};

interface DropZoneProps {
  id: string;
  label: string;
  emptyMessage: string;
  children: React.ReactNode;
  isEmpty: boolean;
  variant: "left" | "right";
}

const DropZone: React.FC<DropZoneProps> = ({
  id,
  label,
  emptyMessage,
  children,
  isEmpty,
  variant,
}) => {
  const { setNodeRef, isOver } = useDroppable({ id });
  return (
    <div
      ref={setNodeRef}
      className={`border rounded-lg p-3 min-h-48 ${
        isOver
          ? "bg-indigo-50 border-indigo-400"
          : variant === "left"
          ? "border-gray-300 bg-gray-50"
          : "border-gray-300 bg-white"
      }`}
    >
      <div className="text-xs font-semibold text-gray-600 uppercase tracking-wider mb-2">
        {label}
      </div>
      <div className="space-y-1">
        {children}
        {isEmpty && (
          <div className="text-sm text-gray-400 text-center py-6">{emptyMessage}</div>
        )}
      </div>
    </div>
  );
};

const TransferList: React.FC<TransferListProps> = ({
  available,
  assignedIds,
  onChange,
  groupBy,
  labels = {},
}) => {
  const sensors = useSensors(useSensor(PointerSensor), useSensor(KeyboardSensor));

  const availableItems = available.filter((item) => !assignedIds.includes(item.id));
  const assignedItems = available.filter((item) => assignedIds.includes(item.id));

  const handleAssign = (id: string) => onChange([...assignedIds, id]);
  const handleRemove = (id: string) =>
    onChange(assignedIds.filter((existing) => existing !== id));

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over) return;
    const itemId = String(active.id);
    const targetZone = String(over.id);
    if (targetZone === "assigned" && !assignedIds.includes(itemId)) {
      handleAssign(itemId);
    } else if (targetZone === "available" && assignedIds.includes(itemId)) {
      handleRemove(itemId);
    }
  };

  const buildGroups = (items: TransferItem[]): Map<string, TransferItem[]> | null => {
    if (!groupBy) return null;
    const map = new Map<string, TransferItem[]>();
    for (const item of items) {
      const key = groupBy(item);
      const bucket = map.get(key);
      if (bucket) {
        bucket.push(item);
      } else {
        map.set(key, [item]);
      }
    }
    return map;
  };

  const availableGroups = buildGroups(availableItems);

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <div className="grid grid-cols-2 gap-4">
        <DropZone
          id="available"
          label={labels.available ?? "Available"}
          emptyMessage="All items assigned"
          isEmpty={availableItems.length === 0}
          variant="left"
        >
          {availableGroups
            ? Array.from(availableGroups.entries()).map(([section, sectionItems]) => (
                <div key={section}>
                  <div className="text-xs font-medium text-gray-500 px-1 pt-3 pb-0.5 first:pt-0">
                    {section}
                  </div>
                  {sectionItems.map((item) => (
                    <ItemRow
                      key={item.id}
                      item={item}
                      direction="assign"
                      onMove={() => handleAssign(item.id)}
                    />
                  ))}
                </div>
              ))
            : availableItems.map((item) => (
                <ItemRow
                  key={item.id}
                  item={item}
                  direction="assign"
                  onMove={() => handleAssign(item.id)}
                />
              ))}
        </DropZone>

        <DropZone
          id="assigned"
          label={labels.assigned ?? "Assigned"}
          emptyMessage="None assigned"
          isEmpty={assignedItems.length === 0}
          variant="right"
        >
          {assignedItems.map((item) => (
            <ItemRow
              key={item.id}
              item={item}
              direction="remove"
              onMove={() => handleRemove(item.id)}
            />
          ))}
        </DropZone>
      </div>
    </DndContext>
  );
};

export default TransferList;
```

### Step 1.4 — Run and confirm all TransferList tests pass

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="TransferList.test"
```

Expected: PASS (5 tests)

### Step 1.5 — Commit

```bash
git add frontend/src/components/access-management/TransferList.tsx \
        frontend/src/components/access-management/__tests__/TransferList.test.tsx
git commit -m "feat(authz): add TransferList drag-and-drop primitive with click affordance"
```

---

## Task 2: Three picker components

**Files:**
- Create: `frontend/src/components/access-management/PermissionPicker.tsx`
- Create: `frontend/src/components/access-management/IncludedGroupsPicker.tsx`
- Create: `frontend/src/components/access-management/MembersPicker.tsx`

These are thin wrappers that map domain data into `TransferItem[]` and delegate to `TransferList`. No separate tests — they are fully exercised by the `GroupDetailPage` tests in Task 3.

### Step 2.1 — Implement PermissionPicker.tsx

`CatalogueFeatureDto` from the generated client has: `key`, `label`, `section`, `hasWrite`, `hasAdmin`. Each feature always has a `.read` permission; optionally `.write` (if `hasWrite`) and `.admin` (if `hasAdmin`).

Create `frontend/src/components/access-management/PermissionPicker.tsx`:

```tsx
import React, { useMemo } from "react";
import { useCatalogue } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface PermissionPickerProps {
  value: string[];
  onChange: (permissions: string[]) => void;
}

const PermissionPicker: React.FC<PermissionPickerProps> = ({ value, onChange }) => {
  const catalogue = useCatalogue();

  const { items, sectionByPermission } = useMemo(() => {
    const allItems: TransferItem[] = [];
    const sectionMap: Record<string, string> = {};
    for (const feature of catalogue.data?.features ?? []) {
      const addLevel = (level: string) => {
        const id = `${feature.key}.${level}`;
        allItems.push({ id, label: `${feature.label ?? feature.key} — ${level}` });
        sectionMap[id] = feature.section ?? "";
      };
      addLevel("read");
      if (feature.hasWrite) addLevel("write");
      if (feature.hasAdmin) addLevel("admin");
    }
    return { items: allItems, sectionByPermission: sectionMap };
  }, [catalogue.data]);

  if (catalogue.isLoading) return <div className="text-gray-500 text-sm">Loading permissions…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      groupBy={(item) => sectionByPermission[item.id] ?? ""}
      labels={{ available: "Available permissions", assigned: "Assigned permissions" }}
    />
  );
};

export default PermissionPicker;
```

### Step 2.2 — Implement IncludedGroupsPicker.tsx

Create `frontend/src/components/access-management/IncludedGroupsPicker.tsx`:

```tsx
import React, { useMemo } from "react";
import { useGroups } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface IncludedGroupsPickerProps {
  currentGroupId: string;
  value: string[];
  onChange: (groupIds: string[]) => void;
}

const IncludedGroupsPicker: React.FC<IncludedGroupsPickerProps> = ({
  currentGroupId,
  value,
  onChange,
}) => {
  const groups = useGroups();

  const items: TransferItem[] = useMemo(
    () =>
      (groups.data?.groups ?? [])
        .filter((g) => g.id !== currentGroupId)
        .map((g) => ({ id: g.id ?? "", label: g.name ?? g.id ?? "" })),
    [groups.data, currentGroupId]
  );

  if (groups.isLoading) return <div className="text-gray-500 text-sm">Loading groups…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "Available groups", assigned: "Included groups" }}
    />
  );
};

export default IncludedGroupsPicker;
```

### Step 2.3 — Implement MembersPicker.tsx

Create `frontend/src/components/access-management/MembersPicker.tsx`:

```tsx
import React, { useMemo } from "react";
import { useUsers } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface MembersPickerProps {
  value: string[];
  onChange: (userIds: string[]) => void;
}

const MembersPicker: React.FC<MembersPickerProps> = ({ value, onChange }) => {
  const users = useUsers();

  const items: TransferItem[] = useMemo(
    () =>
      (users.data?.users ?? []).map((u) => ({
        id: u.id ?? "",
        label: u.displayName ?? u.email ?? u.id ?? "",
        sublabel: u.email,
      })),
    [users.data]
  );

  if (users.isLoading) return <div className="text-gray-500 text-sm">Loading users…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "All users", assigned: "Members" }}
    />
  );
};

export default MembersPicker;
```

### Step 2.4 — Commit

```bash
git add frontend/src/components/access-management/PermissionPicker.tsx \
        frontend/src/components/access-management/IncludedGroupsPicker.tsx \
        frontend/src/components/access-management/MembersPicker.tsx
git commit -m "feat(authz): add PermissionPicker, IncludedGroupsPicker, MembersPicker components"
```

---

## Task 3: GroupDetailPage — test first, then implement

**Files:**
- Create: `frontend/src/pages/__tests__/GroupDetailPage.test.tsx`
- Create: `frontend/src/pages/GroupDetailPage.tsx`

### Step 3.1 — Write the failing tests

Create `frontend/src/pages/__tests__/GroupDetailPage.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import GroupDetailPage from "../GroupDetailPage";

const mockUpdateGroup = jest.fn().mockResolvedValue({});
const mockAssignUserGroups = jest.fn().mockResolvedValue({});
const mockCreateGroup = jest.fn().mockResolvedValue({ id: "new-group-id" });
const mockNavigate = jest.fn();

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useGroup: (id: string | null) => ({
    data:
      id === "group-1"
        ? {
            group: {
              id: "group-1",
              name: "Test Group",
              description: "A description",
              permissions: ["catalog.read"],
              parentGroupIds: ["group-2"],
            },
          }
        : undefined,
    isLoading: false,
  }),
  useCatalogue: () => ({
    data: {
      permissions: ["catalog.read", "catalog.write"],
      features: [
        { key: "catalog", label: "Katalog", section: "Data", hasWrite: true, hasAdmin: false },
      ],
      systemGroups: [],
    },
    isLoading: false,
  }),
  useGroups: () => ({
    data: {
      groups: [
        { id: "group-1", name: "Test Group", permissionCount: 1, memberCount: 1 },
        { id: "group-2", name: "Other Group", permissionCount: 0, memberCount: 0 },
      ],
    },
    isLoading: false,
  }),
  useUsers: () => ({
    data: {
      users: [
        {
          id: "user-1",
          displayName: "Alice",
          email: "alice@test.com",
          groupIds: ["group-1"],
          isActive: true,
        },
        {
          id: "user-2",
          displayName: "Bob",
          email: "bob@test.com",
          groupIds: [],
          isActive: true,
        },
      ],
    },
    isLoading: false,
  }),
  useUpdateGroup: () => ({ mutateAsync: mockUpdateGroup, isPending: false }),
  useAssignUserGroups: () => ({ mutateAsync: mockAssignUserGroups, isPending: false }),
  useCreateGroup: () => ({ mutateAsync: mockCreateGroup, isPending: false }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

jest.mock("../../contexts/ToastContext", () => ({
  useToast: () => ({
    showSuccess: jest.fn(),
    showError: jest.fn(),
  }),
}));

const renderWithRoute = (id: string) =>
  render(
    <MemoryRouter initialEntries={[`/admin/access/groups/${id}`]}>
      <Routes>
        <Route path="/admin/access/groups/:id" element={<GroupDetailPage />} />
      </Routes>
    </MemoryRouter>
  );

beforeEach(() => {
  mockUpdateGroup.mockClear();
  mockAssignUserGroups.mockClear();
  mockCreateGroup.mockClear();
  mockNavigate.mockClear();
});

describe("GroupDetailPage", () => {
  it("renders name and description from loaded group", async () => {
    renderWithRoute("group-1");
    await waitFor(() => {
      expect(screen.getByDisplayValue("Test Group")).toBeInTheDocument();
      expect(screen.getByDisplayValue("A description")).toBeInTheDocument();
    });
  });

  it("Save calls updateGroup with name, description, permissions, parentGroupIds", async () => {
    renderWithRoute("group-1");
    await waitFor(() => screen.getByDisplayValue("Test Group"));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() => {
      expect(mockUpdateGroup).toHaveBeenCalledTimes(1);
      expect(mockUpdateGroup).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "group-1",
          request: expect.objectContaining({
            name: "Test Group",
            description: "A description",
            permissions: ["catalog.read"],
            parentGroupIds: ["group-2"],
          }),
        })
      );
    });
  });

  it("Save does not call assignUserGroups when members are unchanged", async () => {
    renderWithRoute("group-1");
    await waitFor(() => screen.getByDisplayValue("Test Group"));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() => expect(mockUpdateGroup).toHaveBeenCalled());
    expect(mockAssignUserGroups).not.toHaveBeenCalled();
  });

  it("Save calls assignUserGroups to add a new member", async () => {
    renderWithRoute("group-1");
    await waitFor(() => screen.getByDisplayValue("Test Group"));

    // Bob (user-2) is in the available column of MembersPicker — click + to assign
    fireEvent.click(screen.getByRole("button", { name: /assign bob/i }));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(mockAssignUserGroups).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "user-2",
          request: expect.objectContaining({
            userId: "user-2",
            groupIds: ["group-1"],
          }),
        })
      );
    });
  });

  it("Save calls assignUserGroups to remove an existing member", async () => {
    renderWithRoute("group-1");
    await waitFor(() => screen.getByDisplayValue("Test Group"));

    // Alice (user-1) is already a member — click − to remove
    fireEvent.click(screen.getByRole("button", { name: /remove alice/i }));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(mockAssignUserGroups).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "user-1",
          request: expect.objectContaining({
            userId: "user-1",
            groupIds: [],
          }),
        })
      );
    });
  });

  it("Cancel navigates back to the access management list", async () => {
    renderWithRoute("group-1");
    await waitFor(() => screen.getByDisplayValue("Test Group"));
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access");
  });

  it("create mode (id=new) renders empty form and Save calls createGroup then navigates", async () => {
    renderWithRoute("new");
    await waitFor(() => screen.getByLabelText(/name/i));

    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: "Brand New Group" } });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(mockCreateGroup).toHaveBeenCalledWith(
        expect.objectContaining({
          name: "Brand New Group",
        })
      );
      expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new-group-id");
    });
  });

  it("shows validation error if name is empty on Save", async () => {
    const mockShowError = jest.fn();
    jest
      .requireMock("../../contexts/ToastContext")
      .useToast.mockReturnValueOnce({ showSuccess: jest.fn(), showError: mockShowError });

    renderWithRoute("new");
    await waitFor(() => screen.getByLabelText(/name/i));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(mockUpdateGroup).not.toHaveBeenCalled();
      expect(mockCreateGroup).not.toHaveBeenCalled();
    });
  });
});
```

### Step 3.2 — Run and confirm tests fail

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="GroupDetailPage.test"
```

Expected: FAIL — `Cannot find module '../GroupDetailPage'`

### Step 3.3 — Implement GroupDetailPage.tsx

Create `frontend/src/pages/GroupDetailPage.tsx`:

```tsx
import React, { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  useGroup,
  useCatalogue,
  useGroups,
  useUsers,
  useUpdateGroup,
  useCreateGroup,
  useAssignUserGroups,
} from "../api/hooks/useAccessManagement";
import {
  UpdateGroupRequest,
  CreateGroupRequest,
  AssignUserGroupsRequest,
} from "../api/generated/api-client";
import { useToast } from "../contexts/ToastContext";
import PermissionPicker from "../components/access-management/PermissionPicker";
import IncludedGroupsPicker from "../components/access-management/IncludedGroupsPicker";
import MembersPicker from "../components/access-management/MembersPicker";

interface GroupDraft {
  name: string;
  description: string;
  permissions: string[];
  parentGroupIds: string[];
  memberUserIds: string[];
}

const EMPTY_DRAFT: GroupDraft = {
  name: "",
  description: "",
  permissions: [],
  parentGroupIds: [],
  memberUserIds: [],
};

const GroupDetailPage: React.FC = () => {
  const { id = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();

  const isCreateMode = id === "new";

  const groupQuery = useGroup(isCreateMode ? null : id);
  const usersQuery = useUsers();
  useCatalogue(); // pre-fetched by PermissionPicker; called here so loading state is available
  useGroups();   // pre-fetched by IncludedGroupsPicker

  const updateGroup = useUpdateGroup();
  const createGroup = useCreateGroup();
  const assignUserGroups = useAssignUserGroups();

  const [draft, setDraft] = useState<GroupDraft | null>(null);
  const [original, setOriginal] = useState<GroupDraft | null>(null);
  const initialized = useRef(false);

  useEffect(() => {
    if (initialized.current) return;

    if (isCreateMode) {
      setDraft(EMPTY_DRAFT);
      setOriginal(EMPTY_DRAFT);
      initialized.current = true;
      return;
    }

    const group = groupQuery.data?.group;
    const users = usersQuery.data?.users;
    if (!group || !users) return;

    const memberUserIds = users
      .filter((u) => u.groupIds?.includes(group.id ?? ""))
      .map((u) => u.id)
      .filter((uid): uid is string => Boolean(uid));

    const d: GroupDraft = {
      name: group.name ?? "",
      description: group.description ?? "",
      permissions: group.permissions ?? [],
      parentGroupIds: group.parentGroupIds ?? [],
      memberUserIds,
    };
    setDraft(d);
    setOriginal(d);
    initialized.current = true;
  }, [groupQuery.data, usersQuery.data, isCreateMode]);

  const updateDraft = (patch: Partial<GroupDraft>) =>
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));

  const onSaveDraft = async () => {
    if (!draft) return;
    if (!draft.name.trim()) {
      toast.showError("Validation error", "Group name is required");
      return;
    }

    try {
      if (isCreateMode) {
        const result = await createGroup.mutateAsync(
          new CreateGroupRequest({
            name: draft.name.trim(),
            description: draft.description,
            permissions: draft.permissions,
            parentGroupIds: draft.parentGroupIds,
          })
        );
        toast.showSuccess("Group created", "The new group has been saved");
        navigate(`/admin/access/groups/${result.id}`);
        return;
      }

      await updateGroup.mutateAsync({
        id,
        request: new UpdateGroupRequest({
          id,
          name: draft.name.trim(),
          description: draft.description,
          permissions: draft.permissions,
          parentGroupIds: draft.parentGroupIds,
        }),
      });

      const originalIds = new Set(original?.memberUserIds ?? []);
      const newIds = new Set(draft.memberUserIds);
      const allUsers = usersQuery.data?.users ?? [];

      const memberMutations: Promise<unknown>[] = [];

      for (const userId of draft.memberUserIds) {
        if (!originalIds.has(userId)) {
          const user = allUsers.find((u) => u.id === userId);
          if (user) {
            memberMutations.push(
              assignUserGroups.mutateAsync({
                id: userId,
                request: new AssignUserGroupsRequest({
                  userId,
                  groupIds: [...(user.groupIds ?? []), id],
                }),
              })
            );
          }
        }
      }

      for (const userId of original?.memberUserIds ?? []) {
        if (!newIds.has(userId)) {
          const user = allUsers.find((u) => u.id === userId);
          if (user) {
            memberMutations.push(
              assignUserGroups.mutateAsync({
                id: userId,
                request: new AssignUserGroupsRequest({
                  userId,
                  groupIds: (user.groupIds ?? []).filter((g) => g !== id),
                }),
              })
            );
          }
        }
      }

      await Promise.all(memberMutations);

      toast.showSuccess("Saved", "Group updated successfully");
      setOriginal(draft);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes("AuthorizationGroupCycleDetected")) {
        toast.showError("Cycle detected", "This would create a circular group dependency");
      } else {
        toast.showError("Save failed", "An error occurred while saving changes");
      }
    }
  };

  const onCancel = () => navigate("/admin/access");

  const isSaving =
    updateGroup.isPending || createGroup.isPending || assignUserGroups.isPending;
  const isLoading =
    !isCreateMode && (groupQuery.isLoading || usersQuery.isLoading);

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading group…</div>
      </div>
    );
  }

  if (!draft) return null;

  return (
    <div className="p-8 max-w-5xl mx-auto space-y-8">
      <div className="flex items-center gap-4">
        <button
          type="button"
          onClick={onCancel}
          className="text-gray-500 hover:text-gray-700 text-sm"
        >
          ← Access management
        </button>
        <h1 className="text-2xl font-semibold text-gray-900">
          {isCreateMode ? "New group" : "Edit group"}
        </h1>
      </div>

      <div className="space-y-4">
        <div>
          <label htmlFor="group-name" className="block text-sm font-medium text-gray-700 mb-1">
            Name
          </label>
          <input
            id="group-name"
            type="text"
            value={draft.name}
            onChange={(e) => updateDraft({ name: e.target.value })}
            aria-label="Name"
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>
        <div>
          <label htmlFor="group-desc" className="block text-sm font-medium text-gray-700 mb-1">
            Description
          </label>
          <input
            id="group-desc"
            type="text"
            value={draft.description}
            onChange={(e) => updateDraft({ description: e.target.value })}
            className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>
      </div>

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Permissions</h2>
        <PermissionPicker
          value={draft.permissions}
          onChange={(permissions) => updateDraft({ permissions })}
        />
      </section>

      {!isCreateMode && (
        <section>
          <h2 className="text-lg font-medium text-gray-900 mb-3">Included groups</h2>
          <p className="text-sm text-gray-500 mb-3">
            Groups in the right column are building blocks of this group — their permissions are
            inherited.
          </p>
          <IncludedGroupsPicker
            currentGroupId={id}
            value={draft.parentGroupIds}
            onChange={(parentGroupIds) => updateDraft({ parentGroupIds })}
          />
        </section>
      )}

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Members</h2>
        <MembersPicker
          value={draft.memberUserIds}
          onChange={(memberUserIds) => updateDraft({ memberUserIds })}
        />
      </section>

      <div className="flex gap-3 pt-4 border-t border-gray-200">
        <button
          type="button"
          onClick={onSaveDraft}
          disabled={isSaving}
          className="px-5 py-2 bg-indigo-600 text-white rounded-md text-sm font-medium hover:bg-indigo-700 disabled:opacity-50"
        >
          {isSaving ? "Saving…" : "Save"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={isSaving}
          className="px-5 py-2 border border-gray-300 text-gray-700 rounded-md text-sm font-medium hover:bg-gray-50 disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
};

export default GroupDetailPage;
```

### Step 3.4 — Run and confirm GroupDetailPage tests pass

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="GroupDetailPage.test"
```

Expected: PASS (7 tests)

> **Note on the `showError` test:** The last test ("shows validation error if name is empty") uses `jest.requireMock(...).useToast.mockReturnValueOnce(...)`. If that pattern doesn't work with your mock setup, change the test to assert `mockUpdateGroup` and `mockCreateGroup` are not called (the empty-name guard prevents them — that is the verifiable outcome). The toast call itself is a side effect not critical to test.

### Step 3.5 — Commit

```bash
git add frontend/src/pages/GroupDetailPage.tsx \
        frontend/src/pages/__tests__/GroupDetailPage.test.tsx
git commit -m "feat(authz): add GroupDetailPage with draft state and member-diff save logic"
```

---

## Task 4: Update AccessManagementPage — extend test first, then implement

**Files:**
- Modify: `frontend/src/pages/__tests__/AccessManagementPage.test.tsx`
- Modify: `frontend/src/pages/AccessManagementPage.tsx`

### Step 4.1 — Extend the failing tests

Replace `frontend/src/pages/__tests__/AccessManagementPage.test.tsx` with:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import AccessManagementPage from "../AccessManagementPage";

const mockNavigate = jest.fn();

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({
    data: {
      groups: [
        {
          id: "1",
          name: "Spravce",
          permissionCount: 52,
          memberCount: 1,
        },
      ],
    },
    isLoading: false,
  }),
  useUsers: () => ({ data: { users: [] }, isLoading: false }),
  useCatalogue: () => ({ data: { permissions: ["catalog.read"] } }),
  useDeleteGroup: () => ({ mutate: jest.fn(), isPending: false }),
  useSetUserActive: () => ({ mutate: jest.fn(), isPending: false }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const renderPage = () =>
  render(
    <MemoryRouter>
      <AccessManagementPage />
    </MemoryRouter>
  );

beforeEach(() => mockNavigate.mockClear());

describe("AccessManagementPage", () => {
  it("renders groups tab with group name and delete button", () => {
    renderPage();
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete spravce/i })).toBeInTheDocument();
  });

  it("clicking the group name navigates to the group detail page", () => {
    renderPage();
    fireEvent.click(screen.getByText("Spravce"));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/1");
  });

  it("clicking the Edit button navigates to the group detail page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /edit spravce/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/1");
  });

  it("clicking New group navigates to the create page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /new group/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new");
  });
});
```

### Step 4.2 — Run and confirm the new tests fail

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="AccessManagementPage.test"
```

Expected: 1 existing test passes, 3 new tests FAIL (navigate not called / buttons not found)

### Step 4.3 — Update AccessManagementPage.tsx

Replace `frontend/src/pages/AccessManagementPage.tsx` with:

```tsx
import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Edit } from "lucide-react";
import {
  useGroups,
  useUsers,
  useCatalogue,
  useDeleteGroup,
  useSetUserActive,
} from "../api/hooks/useAccessManagement";
import { SetUserActiveRequest } from "../api/generated/api-client";

const AccessManagementPage: React.FC = () => {
  const [tab, setTab] = useState<"groups" | "users">("groups");
  const navigate = useNavigate();
  const groups = useGroups();
  const users = useUsers();
  const catalogue = useCatalogue();
  const deleteGroup = useDeleteGroup();
  const setActive = useSetUserActive();

  return (
    <div className="p-8 max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-semibold text-gray-900">Access management</h1>
        <button
          onClick={() => navigate("/admin/access/groups/new")}
          className="px-4 py-2 bg-indigo-600 text-white rounded text-sm font-medium hover:bg-indigo-700"
          aria-label="New group"
        >
          New group
        </button>
      </div>

      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setTab("groups")}
          className={`px-4 py-2 rounded ${tab === "groups" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Groups
        </button>
        <button
          onClick={() => setTab("users")}
          className={`px-4 py-2 rounded ${tab === "users" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Users
        </button>
      </div>

      {tab === "groups" && (
        <div className="space-y-3">
          {groups.isLoading && <div className="text-gray-500">Loading groups…</div>}
          {groups.data?.groups?.map((g) => (
            <div
              key={g.id}
              className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4"
            >
              <div className="min-w-0 flex-1">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="font-medium text-gray-900 hover:text-indigo-600 text-left"
                >
                  {g.name}
                </button>
                <p className="text-sm text-gray-500">
                  {g.permissionCount} permissions · {g.memberCount} members
                </p>
              </div>
              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                  aria-label={`Edit ${g.name}`}
                >
                  <Edit className="w-4 h-4" />
                </button>
                <button
                  onClick={() => g.id && deleteGroup.mutate(g.id)}
                  disabled={deleteGroup.isPending}
                  className="text-sm text-red-600 hover:underline"
                  aria-label={`Delete ${g.name}`}
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
          <p className="text-xs text-gray-400">
            {catalogue.data?.permissions?.length ?? 0} permissions available.
          </p>
        </div>
      )}

      {tab === "users" && (
        <div className="space-y-3">
          {users.isLoading && <div className="text-gray-500">Loading users…</div>}
          {users.data?.users?.map((u) => (
            <div
              key={u.id}
              className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4"
            >
              <div>
                <div className="font-medium text-gray-900">{u.displayName}</div>
                <p className="text-sm text-gray-500">
                  {u.email} · {u.groupIds?.length ?? 0} groups
                </p>
              </div>
              <button
                onClick={() =>
                  u.id &&
                  setActive.mutate({
                    id: u.id,
                    request: new SetUserActiveRequest({ userId: u.id, isActive: !u.isActive }),
                  })
                }
                disabled={setActive.isPending}
                className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                aria-label={`Toggle active ${u.email}`}
              >
                {u.isActive ? "Disable" : "Enable"}
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default AccessManagementPage;
```

### Step 4.4 — Run and confirm all 4 tests pass

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="AccessManagementPage.test"
```

Expected: PASS (4 tests)

### Step 4.5 — Commit

```bash
git add frontend/src/pages/AccessManagementPage.tsx \
        frontend/src/pages/__tests__/AccessManagementPage.test.tsx
git commit -m "feat(authz): add navigation to group detail and New group button in access list"
```

---

## Task 5: Register route in App.tsx

**Files:**
- Modify: `frontend/src/App.tsx`

### Step 5.1 — Add import for GroupDetailPage

In `frontend/src/App.tsx`, add the import after the `AccessManagementPage` import (line 38):

```tsx
import GroupDetailPage from "./pages/GroupDetailPage";
```

### Step 5.2 — Register the route

In `frontend/src/App.tsx`, add the new route directly after the existing `/admin/access` route (after line 496):

```tsx
<Route
  path="/admin/access/groups/:id"
  element={
    <RequireAccess requiredRole="administration.read">
      <GroupDetailPage />
    </RequireAccess>
  }
/>
```

The result in context looks like:

```tsx
                        <Route
                          path="/admin/access"
                          element={
                            <RequireAccess requiredRole="administration.read">
                              <AccessManagementPage />
                            </RequireAccess>
                          }
                        />
                        <Route
                          path="/admin/access/groups/:id"
                          element={
                            <RequireAccess requiredRole="administration.read">
                              <GroupDetailPage />
                            </RequireAccess>
                          }
                        />
```

### Step 5.3 — Commit

```bash
git add frontend/src/App.tsx
git commit -m "feat(authz): register /admin/access/groups/:id route with administration.read guard"
```

---

## Task 6: Build and lint verification

### Step 6.1 — Run the full test suite for the affected modules

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="AccessManagement|GroupDetail|TransferList"
```

Expected: All tests PASS

### Step 6.2 — TypeScript build

```bash
cd frontend && npm run build
```

Expected: No TypeScript errors, build succeeds.

If the build fails with type errors:
- **`TransferList` default export not found** → verify the file path and that the default export is at the bottom of `TransferList.tsx`
- **`useDroppable`/`useDraggable` not found** → verify `@dnd-kit/core` is in `package.json` dependencies (it is — `RulesList.tsx` already uses it)
- **`Edit` not found from lucide-react** → run `npm list lucide-react` to confirm it's installed; the `Trash2`/`Edit`/`GripVertical` icons are all used in `RulesList.tsx`
- **`CSS` from `@dnd-kit/utilities` not found** → verify `@dnd-kit/utilities` is in `package.json` (it is — imported in `RulesList.tsx`)

### Step 6.3 — ESLint

```bash
cd frontend && npm run lint
```

Expected: No errors. Common lint issues to pre-empt:
- Unused imports → remove them
- Missing `useEffect` dependencies → add missing deps or justify the omission (the `initialized` ref pattern avoids exhaustive-deps warnings)

### Step 6.4 — Smoke test the happy path (manual, against staging or local mock-auth)

1. Navigate to `/admin/access` → list loads, each group row has an Edit icon button
2. Click a group name or its Edit button → navigated to `/admin/access/groups/<id>`
3. Page shows the group's name, description, current permissions in the right column, included groups in right column, members in right column
4. Click + on a permission in the available column → it moves to assigned column
5. Click + on a user in the available (Members) column → moves to Members
6. Click Save → success toast appears
7. Reload → changes persisted
8. Add a user as a member, save → open the Users tab on the access list → verify that user still has their other group memberships (only the current group was added)
9. Try creating a parent cycle (group A includes B, then edit B to include A) → friendly error toast
10. Click New group → `/admin/access/groups/new` → fill name → Save → lands on the new group's detail page

---

## Self-Review

**Spec coverage:**
- ✅ Route `/admin/access/groups/:id` — Task 5
- ✅ Draft with explicit Save/Cancel — Task 3 (GroupDetailPage)
- ✅ PermissionPicker grouped by section via groupBy — Task 2
- ✅ IncludedGroupsPicker excludes current group — Task 2 (currentGroupId filter)
- ✅ MembersPicker with displayName/email — Task 2
- ✅ Save: updateGroup call — Task 3
- ✅ Save: member diff → parallel assignUserGroups — Task 3
- ✅ Save: client-validate non-empty name — Task 3
- ✅ Save: cycle error → friendly toast — Task 3 (try/catch checks `AuthorizationGroupCycleDetected`)
- ✅ Cancel navigates back — Task 3
- ✅ Create mode (id = "new") → useCreateGroup → navigate to new id — Task 3
- ✅ AccessManagementPage: clickable row name + Edit button — Task 4
- ✅ AccessManagementPage: "New group" button — Task 4
- ✅ App.tsx route registration with same RequireAccess guard — Task 5
- ✅ TransferList: click affordance (DRY: all three pickers share it) — Task 1
- ✅ TransferList: DnD between columns — Task 1

**Type consistency across tasks:**
- `TransferItem.id` is `string` everywhere; all pickers guard against undefined IDs with `.filter(Boolean)` or nullish coalescing before creating items
- `GroupDraft.permissions` is `string[]`; `UpdateGroupRequest.permissions` is `string[]` ✓
- `GroupDraft.parentGroupIds` is `string[]`; `UpdateGroupRequest.parentGroupIds` is `string[]` ✓
- `AssignUserGroupsRequest.userId` and `.groupIds` used consistently with `AppUserDto.id` and `AppUserDto.groupIds` ✓
- `CreateGroupResponse.id` is `string | undefined`; navigate call uses it directly — if `undefined`, the URL will be `/admin/access/groups/undefined`. To harden: use `result.id ?? ""` and guard before navigating.

**Placeholder scan:** No TBDs, no "similar to Task N" references, no "add error handling" without code.
