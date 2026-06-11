import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import UsersGrid from "../UsersGrid";

const mockNavigate = jest.fn();
const mockSetActiveMutate = jest.fn();
const mockSetCanPackMutate = jest.fn();
const mockCreateLocalUserMutate = jest.fn();

let mockUsersData: { users: unknown[] } = { users: [] };

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useUsers: () => ({ data: mockUsersData, isLoading: false, isError: false }),
  useSetUserActive: () => ({ mutate: mockSetActiveMutate, isPending: false }),
  useSetUserCanPack: () => ({ mutate: mockSetCanPackMutate, isPending: false, isError: false }),
  useCreateLocalUser: () => ({ mutate: mockCreateLocalUserMutate, isPending: false, isError: false }),
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

const dataRowCount = () =>
  screen.getAllByRole("row").length - 1; // minus header row

beforeEach(() => {
  jest.clearAllMocks();
  mockUsersData = { users: [] };
});

describe("UsersGrid", () => {
  it("hides disabled users by default and reveals them via the toggle", () => {
    mockUsersData = {
      users: [
        user({ id: "a", displayName: "Alice", isActive: true }),
        user({ id: "b", displayName: "Bob", isActive: false }),
      ],
    };
    renderGrid();

    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.queryByText("Bob")).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Show disabled"));

    expect(screen.getByText("Bob")).toBeInTheDocument();
  });

  it("filters by source", () => {
    mockUsersData = {
      users: [
        user({ id: "a", displayName: "Alice", source: "Entra" }),
        user({ id: "b", displayName: "Bob", source: "Local" }),
      ],
    };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Local" }));

    expect(screen.queryByText("Alice")).not.toBeInTheDocument();
    expect(screen.getByText("Bob")).toBeInTheDocument();
  });

  it("filters by source to Entra and back to all", () => {
    mockUsersData = {
      users: [
        user({ id: "a", displayName: "Alice", source: "Entra" }),
        user({ id: "b", displayName: "Bob", source: "Local" }),
      ],
    };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Entra" }));
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.queryByText("Bob")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "All" }));
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("Bob")).toBeInTheDocument();
  });

  it("searches by name or email", () => {
    mockUsersData = {
      users: [
        user({ id: "a", displayName: "Alice", email: "alice@test.com" }),
        user({ id: "b", displayName: "Bob", email: "bob@example.com" }),
      ],
    };
    renderGrid();

    const search = screen.getByLabelText("Search users");
    fireEvent.change(search, { target: { value: "example.com" } });

    expect(screen.queryByText("Alice")).not.toBeInTheDocument();
    expect(screen.getByText("Bob")).toBeInTheDocument();
  });

  it("filters to packers only", () => {
    mockUsersData = {
      users: [
        user({ id: "a", displayName: "Alice", canPack: true }),
        user({ id: "b", displayName: "Bob", canPack: false }),
      ],
    };
    renderGrid();

    fireEvent.click(screen.getByLabelText("Packers only"));

    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.queryByText("Bob")).not.toBeInTheDocument();
  });

  it("pages results, showing 20 per page by default", () => {
    mockUsersData = {
      users: Array.from({ length: 25 }, (_, i) =>
        user({ id: `u${i}`, displayName: `User${String(i).padStart(2, "0")}` }),
      ),
    };
    renderGrid();

    expect(dataRowCount()).toBe(20);

    fireEvent.click(screen.getByRole("button", { name: "2" }));

    expect(dataRowCount()).toBe(5);
  });

  it("toggles can-pack and active state through row actions", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", canPack: false, isActive: true })],
    };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /toggle can pack alice/i }));
    expect(mockSetCanPackMutate).toHaveBeenCalledWith({ id: "a", canPack: true });

    fireEvent.click(screen.getByRole("button", { name: /toggle active alice/i }));
    expect(mockSetActiveMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: "a" }),
    );
  });

  it("navigates to the user detail page from the name", () => {
    mockUsersData = { users: [user({ id: "a", displayName: "Alice" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Alice" }));

    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/a");
  });

  it("creates a local operator from the form", () => {
    renderGrid();

    fireEvent.change(screen.getByPlaceholderText("New local operator name"), {
      target: { value: "  New Op  " },
    });
    fireEvent.click(screen.getByRole("button", { name: /create local operator/i }));

    expect(mockCreateLocalUserMutate).toHaveBeenCalledWith("New Op", expect.anything());
  });
});
