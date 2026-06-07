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
  useUsers: () => ({
    data: {
      users: [
        {
          id: "user-1",
          displayName: "Alice",
          email: "alice@test.com",
          groupIds: [],
          isActive: true,
        },
      ],
    },
    isLoading: false,
  }),
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

  it("clicking a user name in Users tab navigates to user detail page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /users/i }));
    const aliceButtons = screen.getAllByRole("button", { name: /alice/i });
    fireEvent.click(aliceButtons[0]);
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });

  it("clicking Edit icon in Users tab navigates to user detail page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /users/i }));
    fireEvent.click(screen.getByRole("button", { name: /edit alice/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });
});
