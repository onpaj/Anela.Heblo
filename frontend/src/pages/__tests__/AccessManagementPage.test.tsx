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
  useSetUserCanPack: () => ({ mutate: jest.fn(), isPending: false }),
  useCreateLocalUser: () => ({ mutate: jest.fn(), isPending: false }),
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

const goToGroups = () => fireEvent.click(screen.getByRole("button", { name: "Groups" }));

describe("AccessManagementPage", () => {
  it("shows the Users tab by default", () => {
    renderPage();
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.queryByText("Spravce")).not.toBeInTheDocument();
  });

  it("clicking a user name navigates to the user detail page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: "Alice" }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });

  it("clicking Edit icon in Users tab navigates to user detail page", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /edit alice/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });

  it("switching to Groups renders the group and its delete button", () => {
    renderPage();
    goToGroups();
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete spravce/i })).toBeInTheDocument();
  });

  it("the New group button lives in the Groups tab and navigates to the create page", () => {
    renderPage();
    expect(screen.queryByRole("button", { name: /new group/i })).not.toBeInTheDocument();

    goToGroups();
    fireEvent.click(screen.getByRole("button", { name: /new group/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new");
  });

  it("clicking the group name navigates to the group detail page", () => {
    renderPage();
    goToGroups();
    fireEvent.click(screen.getByText("Spravce"));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/1");
  });
});
