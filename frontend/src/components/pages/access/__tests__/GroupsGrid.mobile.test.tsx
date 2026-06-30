import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import GroupsGrid from "../GroupsGrid";
import { useIsMobile } from "../../../../hooks/useMediaQuery";

const mockNavigate = jest.fn();
const mockDeleteMutate = jest.fn();

let mockGroupsData: { groups: unknown[] } = { groups: [] };

jest.mock("../../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => true),
}));

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({ data: mockGroupsData, isLoading: false, isError: false }),
  useCatalogue: () => ({ data: { permissions: [] } }),
  useDeleteGroup: () => ({ mutate: mockDeleteMutate, isPending: false }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const group = (overrides: Record<string, unknown>) => ({
  id: "id",
  name: "Group",
  description: "A group",
  permissionCount: 0,
  memberCount: 0,
  parentCount: 0,
  ...overrides,
});

const renderGrid = () =>
  render(
    <MemoryRouter>
      <GroupsGrid />
    </MemoryRouter>,
  );

beforeEach(() => {
  jest.clearAllMocks();
  // resetMocks:true in CRA config resets implementations; restore mobile mode.
  (useIsMobile as jest.Mock).mockReturnValue(true);
  mockGroupsData = { groups: [] };
});

describe("GroupsGrid — mobile cards", () => {
  it("renders a card with the group name and a stats line (no table)", () => {
    mockGroupsData = {
      groups: [
        group({
          id: "a",
          name: "Admins",
          permissionCount: 12,
          memberCount: 5,
          parentCount: 2,
        }),
      ],
    };
    renderGrid();

    expect(screen.getByText("Admins")).toBeInTheDocument();
    expect(
      screen.getByText(/12 permissions · 5 members · 2 parents/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("navigates to the detail page when the name is tapped", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Admins" }));

    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/a");
  });

  it("deletes a group from the card", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /delete admins/i }));

    expect(mockDeleteMutate).toHaveBeenCalledWith("a");
  });
});
