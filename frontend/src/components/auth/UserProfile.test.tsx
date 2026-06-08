import React from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import UserProfile from "./UserProfile";

const mockBaseAuth = {
  isAuthenticated: true,
  login: jest.fn(),
  logout: jest.fn(),
  inProgress: "none",
  getUserInfo: () => ({
    name: "Test User",
    email: "test@anela.cz",
    initials: "TU",
    roles: ["super_user"],
  }),
  getStoredUserInfo: () => null,
};

interface MockPermissionsCtx {
  permissions: string[];
  isSuperUser: boolean;
  groups: string[];
  isLoading: boolean;
  hasPermission: (p: string) => boolean;
}

let mockCtx: MockPermissionsCtx = {
  permissions: [],
  isSuperUser: false,
  groups: [],
  isLoading: false,
  hasPermission: (_: string) => false,
};

jest.mock("../../auth/useAuth", () => ({ useAuth: () => mockBaseAuth }));
jest.mock("../../auth/mockAuth", () => ({
  useMockAuth: () => mockBaseAuth,
  shouldUseMockAuth: () => false,
}));
jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: () => mockCtx,
}));

const openModal = async () => {
  render(<UserProfile />);
  await userEvent.click(screen.getByRole("button"));
};

describe("UserProfile permissions display", () => {
  it("renders Permissions chips when user has DB permissions", async () => {
    mockCtx = {
      permissions: ["catalog.read", "journal.read"],
      isSuperUser: false,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    };

    await openModal();

    expect(screen.getByText("Oprávnění")).toBeInTheDocument();
    expect(screen.getByText("catalog.read")).toBeInTheDocument();
    expect(screen.getByText("journal.read")).toBeInTheDocument();
  });
});
