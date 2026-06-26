import React from "react";
import { render, screen, fireEvent, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import GroupsGrid from "../GroupsGrid";

const mockNavigate = jest.fn();
const mockDeleteGroupMutate = jest.fn();

let mockGroupsData: { groups: unknown[] } = { groups: [] };

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({ data: mockGroupsData, isLoading: false, isError: false }),
  useCatalogue: () => ({ data: { permissions: ["catalog.read", "catalog.write"] } }),
  useDeleteGroup: () => ({ mutate: mockDeleteGroupMutate, isPending: false }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const group = (overrides: Record<string, unknown>) => ({
  id: "id",
  name: "Group",
  description: "",
  permissionCount: 0,
  parentCount: 0,
  memberCount: 0,
  ...overrides,
});

const renderGrid = () =>
  render(
    <MemoryRouter>
      <GroupsGrid />
    </MemoryRouter>,
  );

const dataRowNames = () =>
  screen
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("button")[0].textContent);

beforeEach(() => {
  jest.clearAllMocks();
  mockGroupsData = { groups: [] };
});

describe("GroupsGrid", () => {
  it("searches by name or description", () => {
    mockGroupsData = {
      groups: [
        group({ id: "a", name: "Admins", description: "full access" }),
        group({ id: "b", name: "Readers", description: "view only" }),
      ],
    };
    renderGrid();

    fireEvent.change(screen.getByLabelText("Search groups"), {
      target: { value: "view only" },
    });

    expect(screen.queryByText("Admins")).not.toBeInTheDocument();
    expect(screen.getByText("Readers")).toBeInTheDocument();
  });

  it("sorts by a column when its header is clicked", () => {
    mockGroupsData = {
      groups: [
        group({ id: "a", name: "Beta" }),
        group({ id: "b", name: "Alpha" }),
      ],
    };
    renderGrid();

    // Default sort is by name ascending.
    expect(dataRowNames()).toEqual(["Alpha", "Beta"]);

    // Click the Name header to flip to descending.
    fireEvent.click(screen.getByText("Name"));
    expect(dataRowNames()).toEqual(["Beta", "Alpha"]);
  });

  it("wires up edit and delete row actions", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /edit admins/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/a");

    fireEvent.click(screen.getByRole("button", { name: /delete admins/i }));
    expect(mockDeleteGroupMutate).toHaveBeenCalledWith("a");
  });

  it("navigates to the create page from the New group button", () => {
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /new group/i }));
    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/new");
  });

  it("shows the available permission count", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    expect(screen.getByText(/2 permissions available/i)).toBeInTheDocument();
  });
});
