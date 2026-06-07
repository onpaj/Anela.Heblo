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
  useEntraAccessUsers: () => ({ data: { users: [] }, isLoading: false }),
  useAddGroupMember: () => ({ mutateAsync: jest.fn().mockResolvedValue({}), isPending: false }),
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
  mockCreateGroup.mockResolvedValue({ id: "new-group-id" });
  mockNavigate.mockClear();
});

describe("GroupDetailPage", () => {
  it("renders name and description from loaded group", async () => {
    renderWithRoute("group-1");
    expect(await screen.findByDisplayValue("Test Group")).toBeInTheDocument();
    expect(await screen.findByDisplayValue("A description")).toBeInTheDocument();
  });

  it("Save calls updateGroup with name, description, permissions, parentGroupIds", async () => {
    renderWithRoute("group-1");
    await screen.findByDisplayValue("Test Group");
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() =>
      expect(mockUpdateGroup).toHaveBeenCalledTimes(1)
    );
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

  it("Save does not call assignUserGroups when members are unchanged", async () => {
    renderWithRoute("group-1");
    await screen.findByDisplayValue("Test Group");
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() => expect(mockUpdateGroup).toHaveBeenCalled());
    expect(mockAssignUserGroups).not.toHaveBeenCalled();
  });

  it("Save calls assignUserGroups to add a new member", async () => {
    renderWithRoute("group-1");
    await screen.findByDisplayValue("Test Group");

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
    await screen.findByDisplayValue("Test Group");

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
    await screen.findByDisplayValue("Test Group");
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access");
  });

  it("create mode (id=new) renders empty form and Save calls createGroup then navigates", async () => {
    renderWithRoute("new");
    await screen.findByLabelText(/name/i);

    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: "Brand New Group" } });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(mockCreateGroup).toHaveBeenCalledWith(
        expect.objectContaining({
          name: "Brand New Group",
        })
      )
    );
    await waitFor(() =>
      expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new-group-id")
    );
  });

  it("shows validation error if name is empty on Save", async () => {
    renderWithRoute("new");
    await screen.findByLabelText(/name/i);
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => expect(mockUpdateGroup).not.toHaveBeenCalled());
    expect(mockCreateGroup).not.toHaveBeenCalled();
  });
});
