import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import UserDetailPage from "../UserDetailPage";

const mockAssignUserGroups = jest.fn().mockResolvedValue({});
const mockUpdateUser = jest.fn().mockResolvedValue({});
const mockSetActive = jest.fn();
const mockNavigate = jest.fn();

jest.mock("../../api/hooks/useAccessManagement", () => ({
  useUsers: () => ({
    data: {
      users: [
        {
          id: "user-1",
          displayName: "Alice",
          email: "alice@test.com",
          groupIds: ["g1"],
          isActive: true,
          canPack: false,
          lastLoginAt: null,
        },
      ],
    },
    isLoading: false,
  }),
  useAssignUserGroups: () => ({ mutateAsync: mockAssignUserGroups, isPending: false }),
  useUpdateUser: () => ({ mutateAsync: mockUpdateUser, isPending: false }),
  useSetUserActive: () => ({ mutateAsync: mockSetActive, isPending: false }),
  useUserPermissions: () => ({
    data: { permissions: ["catalog.read", "orders.write"] },
    isLoading: false,
  }),
  useGroups: () => ({
    data: {
      groups: [
        { id: "g1", name: "Admins" },
        { id: "g2", name: "Editors" },
      ],
    },
    isLoading: false,
  }),
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
    <MemoryRouter initialEntries={[`/admin/access/users/${id}`]}>
      <Routes>
        <Route path="/admin/access/users/:id" element={<UserDetailPage />} />
      </Routes>
    </MemoryRouter>
  );

beforeEach(() => {
  mockAssignUserGroups.mockClear();
  mockUpdateUser.mockClear();
  mockSetActive.mockClear();
  mockNavigate.mockClear();
});

describe("UserDetailPage", () => {
  it("renders user displayName and email", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText("Alice")).toBeInTheDocument();
    expect(screen.getByDisplayValue("alice@test.com")).toBeInTheDocument();
  });

  it("shows 'Never logged in' when lastLoginAt is null", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText(/never logged in/i)).toBeInTheDocument();
  });

  it("Save calls assignUserGroups with userId and current groupIds", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    await waitFor(() =>
      expect(mockAssignUserGroups).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "user-1",
          request: expect.objectContaining({
            userId: "user-1",
            groupIds: ["g1"],
          }),
        })
      )
    );
  });

  it("Save calls updateUser with edited displayName, email and canPack", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");

    fireEvent.change(screen.getByDisplayValue("Alice"), {
      target: { value: "Alice Updated" },
    });
    fireEvent.change(screen.getByDisplayValue("alice@test.com"), {
      target: { value: "alice2@test.com" },
    });
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(mockUpdateUser).toHaveBeenCalledWith(
        expect.objectContaining({
          id: "user-1",
          request: expect.objectContaining({
            userId: "user-1",
            displayName: "Alice Updated",
            email: "alice2@test.com",
            canPack: true,
          }),
        })
      )
    );
  });

  it("blocks Save and does not call updateUser when displayName is blank", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");

    fireEvent.change(screen.getByDisplayValue("Alice"), { target: { value: "  " } });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => expect(mockUpdateUser).not.toHaveBeenCalled());
    expect(mockAssignUserGroups).not.toHaveBeenCalled();
  });

  it("enable/disable button calls setActive with toggled isActive", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /disable user/i }));
    expect(mockSetActive).toHaveBeenCalledWith(
      expect.objectContaining({
        id: "user-1",
        request: expect.objectContaining({ userId: "user-1", isActive: false }),
      })
    );
  });

  it("renders effective permissions from useUserPermissions", async () => {
    renderWithRoute("user-1");
    expect(await screen.findByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("orders.write")).toBeInTheDocument();
  });

  it("Cancel with no changes navigates straight to the Users tab", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users");
    expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
  });

  it("Cancel with unsaved changes opens the dialog; Discard navigates to the Users tab", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.change(screen.getByDisplayValue("Alice"), {
      target: { value: "Alice Updated" },
    });
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /discard changes/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users");
  });

  it("Keep editing dismisses the dialog without navigating", async () => {
    renderWithRoute("user-1");
    await screen.findByText("Alice");
    fireEvent.change(screen.getByDisplayValue("Alice"), {
      target: { value: "Alice Updated" },
    });
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    fireEvent.click(screen.getByRole("button", { name: /keep editing/i }));

    expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
    expect(mockNavigate).not.toHaveBeenCalled();
  });
});
