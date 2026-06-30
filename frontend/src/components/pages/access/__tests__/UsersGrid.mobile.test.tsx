import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import UsersGrid from "../UsersGrid";
import { useIsMobile } from "../../../../hooks/useMediaQuery";

const mockNavigate = jest.fn();
const mockSetActiveMutate = jest.fn();
const mockSetCanPackMutate = jest.fn();
const mockCreateLocalUserMutate = jest.fn();

let mockUsersData: { users: unknown[] } = { users: [] };

jest.mock("../../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => true),
}));

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useUsers: () => ({ data: mockUsersData, isLoading: false, isError: false }),
  useSetUserActive: () => ({ mutate: mockSetActiveMutate, isPending: false }),
  useSetUserCanPack: () => ({
    mutate: mockSetCanPackMutate,
    isPending: false,
    isError: false,
  }),
  useCreateLocalUser: () => ({
    mutate: mockCreateLocalUserMutate,
    isPending: false,
    isError: false,
  }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const user = (overrides: Record<string, unknown>) => ({
  id: "id",
  displayName: "Name",
  email: "name@test.com",
  source: "Entra",
  isActive: true,
  canPack: false,
  groupIds: [],
  lastLoginAt: undefined,
  ...overrides,
});

const renderGrid = () =>
  render(
    <MemoryRouter>
      <UsersGrid />
    </MemoryRouter>,
  );

beforeEach(() => {
  jest.clearAllMocks();
  // resetMocks:true in CRA config resets implementations; restore mobile mode.
  (useIsMobile as jest.Mock).mockReturnValue(true);
  mockUsersData = { users: [] };
});

describe("UsersGrid — mobile cards", () => {
  it("renders a card with the user's name and email (no table)", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", email: "alice@test.com" })],
    };
    renderGrid();

    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("navigates to the detail page when the name is tapped", () => {
    mockUsersData = { users: [user({ id: "a", displayName: "Alice" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Alice" }));

    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/a");
  });

  it("toggles active state from the card", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", isActive: true })],
    };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /toggle active alice/i }));

    expect(mockSetActiveMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: "a" }),
    );
  });

  it("toggles can-pack from the card", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", canPack: false })],
    };
    renderGrid();

    fireEvent.click(
      screen.getByRole("button", { name: /toggle can pack alice/i }),
    );

    expect(mockSetCanPackMutate).toHaveBeenCalledWith({ id: "a", canPack: true });
  });
});
