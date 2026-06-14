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

const renderPage = (path = "/admin/access/users") =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AccessManagementPage />
    </MemoryRouter>
  );

beforeEach(() => mockNavigate.mockClear());

describe("AccessManagementPage", () => {
  it("shows the Users tab on the /users route", () => {
    renderPage("/admin/access/users");
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.queryByText("Spravce")).not.toBeInTheDocument();
  });

  it("shows the Groups tab on the /groups route", () => {
    renderPage("/admin/access/groups");
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.queryByText("Alice")).not.toBeInTheDocument();
  });

  it("clicking the Groups tab navigates to its URL", () => {
    renderPage("/admin/access/users");
    fireEvent.click(screen.getByRole("button", { name: "Groups" }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups");
  });

  it("clicking the Users tab navigates to its URL", () => {
    renderPage("/admin/access/groups");
    fireEvent.click(screen.getByRole("button", { name: "Users" }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users");
  });

  it("clicking a user name navigates to the user detail page", () => {
    renderPage("/admin/access/users");
    fireEvent.click(screen.getByRole("button", { name: "Alice" }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });

  it("clicking Edit icon in Users tab navigates to user detail page", () => {
    renderPage("/admin/access/users");
    fireEvent.click(screen.getByRole("button", { name: /edit alice/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/user-1");
  });

  it("Groups tab renders the group and its delete button", () => {
    renderPage("/admin/access/groups");
    expect(screen.getByText("Spravce")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete spravce/i })).toBeInTheDocument();
  });

  it("the New group button lives in the Groups tab and navigates to the create page", () => {
    renderPage("/admin/access/users");
    expect(screen.queryByRole("button", { name: /new group/i })).not.toBeInTheDocument();

    renderPage("/admin/access/groups");
    fireEvent.click(screen.getByRole("button", { name: /new group/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new");
  });

  it("clicking the group name navigates to the group detail page", () => {
    renderPage("/admin/access/groups");
    fireEvent.click(screen.getByText("Spravce"));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/1");
  });
});
